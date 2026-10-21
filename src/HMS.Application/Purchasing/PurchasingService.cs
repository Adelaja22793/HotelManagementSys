using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Common;
using HMS.Application.Inventory;
using HMS.Contracts.Purchasing;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Purchasing;

public interface IPurchasingService
{
    Task<IReadOnlyList<RequisitionDto>> ListRequisitionsAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<RequisitionDto> CreateRequisitionAsync(Guid branchId, CreateRequisitionRequest request, CancellationToken ct = default);
    Task<RequisitionDto> DecideRequisitionAsync(Guid branchId, Guid requisitionId, DecisionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrderDto>> ListOrdersAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<PurchaseOrderDto> GetOrderAsync(Guid branchId, Guid orderId, CancellationToken ct = default);
    Task<PurchaseOrderDto> CreateOrderAsync(Guid branchId, CreatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<PurchaseOrderDto> ApproveOrderAsync(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct = default);
    Task<PurchaseOrderDto> CancelOrderAsync(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct = default);
    Task<PurchaseOrderDto> CloseOrderAsync(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct = default);
    Task<GoodsReceiptDto> ReceiveAsync(Guid branchId, Guid orderId, ReceiveGoodsRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<GoodsReceiptDto>> ListReceiptsAsync(Guid branchId, Guid orderId, CancellationToken ct = default);
    Task<SupplierInvoiceDto> CreateInvoiceAsync(Guid branchId, CreateSupplierInvoiceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierInvoiceDto>> ListInvoicesAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<SupplierInvoiceDto> ApproveInvoiceAsync(Guid branchId, Guid invoiceId, PurchaseOrderActionRequest request, CancellationToken ct = default);
    Task<SupplierPaymentDto> PayInvoiceAsync(Guid branchId, Guid invoiceId, PaySupplierRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<UninvoicedReceiptDto>> UninvoicedReceiptsAsync(Guid branchId, CancellationToken ct = default);
}

internal sealed class PurchasingService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    INumberSequence numbers,
    IStockService stock,
    ILedgerService ledger) : IPurchasingService
{
    public async Task<IReadOnlyList<RequisitionDto>> ListRequisitionsAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.PurchaseRequisitions.AsNoTracking().Include(r => r.Lines).ThenInclude(l => l.Item).Where(r => r.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<RequisitionStatus>(status, nameof(status));
            q = q.Where(r => r.Status == parsed);
        }
        return (await q.OrderByDescending(r => r.CreatedAtUtc).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<RequisitionDto> CreateRequisitionAsync(Guid branchId, CreateRequisitionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (request.Lines is null || request.Lines.Count == 0) throw new ValidationAppException(nameof(request.Lines), "Add at least one line.");
        await RequireItemsAsync(branchId, request.Lines.Select(l => l.ItemId), ct);
        if (request.Lines.Any(l => l.Quantity <= 0)) throw new ValidationAppException(nameof(request.Lines), "Quantities must be positive.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Requisition, ct);
        var requisition = new PurchaseRequisition(scope.CompanyId, branchId, number, scope.UserId, request.Notes?.Trim());
        foreach (var line in request.Lines)
            requisition.Lines.Add(new PurchaseRequisitionLine { RequisitionId = requisition.Id, ItemId = line.ItemId, Quantity = line.Quantity, EstimatedUnitCost = line.EstimatedUnitCost });
        db.PurchaseRequisitions.Add(requisition);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetRequisitionAsync(requisition.Id, ct);
    }

    public async Task<RequisitionDto> DecideRequisitionAsync(Guid branchId, Guid requisitionId, DecisionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var requisition = await db.PurchaseRequisitions.GetOwnedAsync(branchId, requisitionId, "Requisition", ct);
        BranchOperations.EnsureStamp(requisition, request.ConcurrencyStamp, "requisition");
        if (request.Approved) requisition.Approve(scope.UserId, scope.UtcNow);
        else requisition.Reject(scope.UserId, request.Reason ?? string.Empty, scope.UtcNow);
        await db.SaveChangesAsync(ct);
        return await GetRequisitionAsync(requisition.Id, ct);
    }

    public async Task<IReadOnlyList<PurchaseOrderDto>> ListOrdersAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = OrdersQuery().Where(o => o.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<PurchaseOrderStatus>(status, nameof(status));
            q = q.Where(o => o.Status == parsed);
        }
        return (await q.OrderByDescending(o => o.CreatedAtUtc).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<PurchaseOrderDto> GetOrderAsync(Guid branchId, Guid orderId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return ToDto(await OrdersQuery().SingleOrDefaultAsync(o => o.Id == orderId && o.BranchId == branchId, ct) ?? throw new NotFoundException("Purchase order", orderId));
    }

    public async Task<PurchaseOrderDto> CreateOrderAsync(Guid branchId, CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var supplier = await db.Suppliers.GetOwnedAsync(branchId, request.SupplierId, "Supplier", ct);
        if (!supplier.IsActive) throw new ConflictException("SUPPLIER_INACTIVE", $"{supplier.Name} is inactive.");
        var location = await db.StockLocations.GetOwnedAsync(branchId, request.DeliveryLocationId, "Stock location", ct);
        if (!location.IsActive) throw new ConflictException("LOCATION_INACTIVE", $"{location.Name} is inactive.");

        PurchaseRequisition? requisition = null;
        var lines = request.Lines?.ToList() ?? [];
        if (request.RequisitionId is { } reqId)
        {
            requisition = await db.PurchaseRequisitions.Include(r => r.Lines).GetOwnedAsync(branchId, reqId, "Requisition", ct);
            if (lines.Count == 0)
            {
                if (requisition.Lines.Any(l => l.EstimatedUnitCost is null))
                    throw new ValidationAppException(nameof(request.Lines), "The requisition has lines without a cost; send priced lines for the order.");
                lines = requisition.Lines.Select(l => new PurchaseOrderLineRequest(l.ItemId, l.Quantity, l.EstimatedUnitCost!.Value)).ToList();
            }
        }
        if (lines.Count == 0) throw new ValidationAppException(nameof(request.Lines), "Add at least one line.");
        if (lines.Any(l => l.Quantity <= 0 || l.UnitCost < 0)) throw new ValidationAppException(nameof(request.Lines), "Quantities must be positive and costs not negative.");
        await RequireItemsAsync(branchId, lines.Select(l => l.ItemId), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.PurchaseOrder, ct);
        var order = new PurchaseOrder(scope.CompanyId, branchId, number, supplier.Id, location.Id, scope.BusinessDate, scope.UserId, requisition?.Id);
        foreach (var l in lines)
            order.Lines.Add(new PurchaseOrderLine { PurchaseOrderId = order.Id, ItemId = l.ItemId, Quantity = l.Quantity, UnitCost = Math.Round(l.UnitCost, 4) });
        requisition?.MarkOrdered(order.Id);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetOrderAsync(branchId, order.Id, ct);
    }

    public Task<PurchaseOrderDto> ApproveOrderAsync(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct = default) =>
        MutateOrderAsync(branchId, orderId, request.ConcurrencyStamp, (o, s) => o.Approve(s.UserId, s.UtcNow), ct);

    public Task<PurchaseOrderDto> CancelOrderAsync(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct = default) =>
        MutateOrderAsync(branchId, orderId, request.ConcurrencyStamp, (o, _) => o.Cancel(), ct);

    public Task<PurchaseOrderDto> CloseOrderAsync(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct = default) =>
        MutateOrderAsync(branchId, orderId, request.ConcurrencyStamp, (o, _) => o.Close(), ct);

    public async Task<GoodsReceiptDto> ReceiveAsync(Guid branchId, Guid orderId, ReceiveGoodsRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var key = InventoryService.RequireKey(idempotencyKey);
        var replay = await db.GoodsReceipts.AsNoTracking().Include(g => g.Lines).SingleOrDefaultAsync(g => g.BranchId == branchId && g.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            if (replay.PurchaseOrderId != orderId) throw new ConflictException("IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was used for another receipt.");
            return ToDto(replay);
        }
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity <= 0))
            throw new ValidationAppException(nameof(request.Lines), "Enter a positive quantity for at least one line.");

        var order = await db.PurchaseOrders.Include(o => o.Lines).GetOwnedAsync(branchId, orderId, "Purchase order", ct);
        order.EnsureReceivable();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.GoodsReceipt, ct);
        var receipt = new GoodsReceipt(scope.CompanyId, branchId, number, order.Id, order.SupplierId, order.DeliveryLocationId, scope.BusinessDate,
            request.DeliveryNote?.Trim(), key, scope.UserId, scope.UtcNow);
        foreach (var group in request.Lines.GroupBy(l => l.PurchaseOrderLineId))
        {
            var poLine = order.Lines.SingleOrDefault(l => l.Id == group.Key) ?? throw new NotFoundException("Purchase order line", group.Key);
            var quantity = group.Sum(l => l.Quantity);
            if (poLine.ReceivedQuantity + quantity > poLine.Quantity)
                throw new ConflictException("OVER_RECEIPT", $"Only {poLine.Quantity - poLine.ReceivedQuantity:0.###} more can be received on this line. Raise a new order for extra goods.");
            poLine.ReceivedQuantity += quantity;
            var line = new GoodsReceiptLine { GoodsReceiptId = receipt.Id, PurchaseOrderLineId = poLine.Id, ItemId = poLine.ItemId, Quantity = quantity, UnitCost = poLine.UnitCost };
            receipt.Lines.Add(line);
            await stock.AddAsync(scope, poLine.ItemId, order.DeliveryLocationId, quantity, poLine.UnitCost, StockMovementType.Receipt,
                "GoodsReceiptLine", line.Id.ToString(), number, ct);
        }
        order.RefreshReceiptStatus();
        db.Touch(order);
        db.GoodsReceipts.Add(receipt);
        await ledger.PostAsync(scope, "GoodsReceipt", receipt.Id.ToString(), $"Goods received {number} against {order.OrderNumber}",
            [LedgerLine.Dr(AccountKeys.Inventory, receipt.Total), LedgerLine.Cr(AccountKeys.GoodsReceivedNotInvoiced, receipt.Total)], ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.GoodsReceipts.AsNoTracking().Include(g => g.Lines).SingleOrDefaultAsync(g => g.BranchId == branchId && g.IdempotencyKey == key, ct);
            return winner is not null ? ToDto(winner) : throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The order changed while receiving. Reload and try again.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The order or stock changed while receiving. Reload and try again.");
        }
        return ToDto(receipt);
    }

    public async Task<IReadOnlyList<GoodsReceiptDto>> ListReceiptsAsync(Guid branchId, Guid orderId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(g => g.Lines).Where(g => g.BranchId == branchId && g.PurchaseOrderId == orderId)
            .OrderBy(g => g.ReceivedAtUtc).ToListAsync(ct);
        return receipts.Select(ToDto).ToList();
    }

    public async Task<SupplierInvoiceDto> CreateInvoiceAsync(Guid branchId, CreateSupplierInvoiceRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.SupplierReference), request.SupplierReference, 100)
            .When(request.Lines is null || request.Lines.Count == 0, nameof(request.Lines), "Add at least one line.")
            .ThrowIfInvalid();
        if (request.Lines!.Any(l => l.Quantity <= 0 || l.UnitPrice < 0))
            throw new ValidationAppException(nameof(request.Lines), "Quantities must be positive and prices not negative.");

        var order = await db.PurchaseOrders.Include(o => o.Lines).ThenInclude(l => l.Item).Include(o => o.Supplier).GetOwnedAsync(branchId, request.PurchaseOrderId, "Purchase order", ct);
        if (order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled)
            throw new ConflictException("PO_STATE", "Invoices can only be entered against approved orders.");
        var reference = request.SupplierReference.Trim();
        if (await db.SupplierInvoices.AnyAsync(i => i.BranchId == branchId && i.SupplierId == order.SupplierId && i.SupplierReference == reference, ct))
            throw new ConflictException("DUPLICATE_SUPPLIER_INVOICE", $"Invoice {reference} from {order.Supplier!.Name} has already been entered.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.SupplierInvoice, ct);
        var invoice = new SupplierInvoice(scope.CompanyId, branchId, number, order.SupplierId, order.Id, reference, request.InvoiceDate,
            request.InvoiceDate.AddDays(order.Supplier!.PaymentTermsDays), scope.UserId);

        var issues = new List<string>();
        decimal total = 0, receivedValue = 0;
        foreach (var l in request.Lines!)
        {
            var poLine = order.Lines.SingleOrDefault(x => x.Id == l.PurchaseOrderLineId) ?? throw new NotFoundException("Purchase order line", l.PurchaseOrderLineId);
            var amount = Money.Round(l.Quantity * l.UnitPrice);
            var uninvoiced = poLine.ReceivedQuantity - poLine.InvoicedQuantity;
            if (l.Quantity > uninvoiced) issues.Add($"{poLine.Item?.Sku}: billed {l.Quantity:0.###} but only {uninvoiced:0.###} received and not yet invoiced.");
            if (l.UnitPrice != Money.Round(poLine.UnitCost)) issues.Add($"{poLine.Item?.Sku}: price {l.UnitPrice:N2} differs from order price {poLine.UnitCost:N2}.");
            poLine.InvoicedQuantity += l.Quantity;
            total += amount;
            receivedValue += Money.Round(l.Quantity * poLine.UnitCost);
            invoice.Lines.Add(new SupplierInvoiceLine { SupplierInvoiceId = invoice.Id, PurchaseOrderLineId = poLine.Id, ItemId = poLine.ItemId, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Amount = amount });
        }
        invoice.SetTotals(total, receivedValue, issues.Count == 0 ? null : string.Join("\n", issues));
        db.SupplierInvoices.Add(invoice);
        db.Touch(order);
        // Matched invoices post at once; discrepancies post when approved.
        if (invoice.Status == SupplierInvoiceStatus.Matched) await PostInvoiceAsync(scope, invoice, order, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetInvoiceAsync(invoice.Id, ct);
    }

    public async Task<IReadOnlyList<SupplierInvoiceDto>> ListInvoicesAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.SupplierInvoices.AsNoTracking().Include(i => i.Supplier).Include(i => i.Lines).Where(i => i.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<SupplierInvoiceStatus>(status, nameof(status));
            q = q.Where(i => i.Status == parsed);
        }
        return (await q.OrderBy(i => i.DueDate).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<SupplierInvoiceDto> ApproveInvoiceAsync(Guid branchId, Guid invoiceId, PurchaseOrderActionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var invoice = await db.SupplierInvoices.Include(i => i.Lines).GetOwnedAsync(branchId, invoiceId, "Supplier invoice", ct);
        BranchOperations.EnsureStamp(invoice, request.ConcurrencyStamp, "invoice");
        var order = await db.PurchaseOrders.SingleAsync(o => o.Id == invoice.PurchaseOrderId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        invoice.ApproveDiscrepancy(scope.UserId);
        await PostInvoiceAsync(scope, invoice, order, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetInvoiceAsync(invoice.Id, ct);
    }

    public async Task<SupplierPaymentDto> PayInvoiceAsync(Guid branchId, Guid invoiceId, PaySupplierRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var key = InventoryService.RequireKey(idempotencyKey);
        var tender = EnumParser.Parse<TenderType>(request.Tender, nameof(request.Tender));
        if (tender is not (TenderType.BankTransfer or TenderType.Cash))
            throw new ValidationAppException(nameof(request.Tender), "Suppliers are paid by BankTransfer or Cash.");
        var replay = await db.SupplierPayments.AsNoTracking().SingleOrDefaultAsync(p => p.BranchId == branchId && p.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            if (replay.SupplierInvoiceId != invoiceId || replay.Amount != request.Amount)
                throw new ConflictException("IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was used for a different payment.");
            return ToDto(replay);
        }

        var invoice = await db.SupplierInvoices.GetOwnedAsync(branchId, invoiceId, "Supplier invoice", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        invoice.RecordPayment(request.Amount);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.SupplierPayment, ct);
        var payment = new SupplierPayment(scope.CompanyId, branchId, number, invoice.Id, request.Amount, tender, request.Reference ?? string.Empty,
            scope.BusinessDate, key, scope.UserId, scope.UtcNow);
        db.SupplierPayments.Add(payment);
        await ledger.PostAsync(scope, "SupplierPayment", payment.Id.ToString(), $"Payment {number} for supplier invoice {invoice.SupplierReference}",
            [LedgerLine.Dr(AccountKeys.AccountsPayable, payment.Amount), LedgerLine.Cr(LedgerService.TenderKey(tender), payment.Amount, payment.Reference)], ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The invoice was paid by someone else at the same time. Reload and check the balance.");
        }
        return ToDto(payment);
    }

    public async Task<IReadOnlyList<UninvoicedReceiptDto>> UninvoicedReceiptsAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var orders = await OrdersQuery().Where(o => o.BranchId == branchId && o.Status != PurchaseOrderStatus.Cancelled
            && o.Lines.Any(l => l.ReceivedQuantity > l.InvoicedQuantity)).ToListAsync(ct);
        return orders.SelectMany(o => o.Lines.Where(l => l.ReceivedQuantity > l.InvoicedQuantity).Select(l =>
                new UninvoicedReceiptDto(o.Id, o.OrderNumber, o.Supplier!.Name, l.Id, l.Item!.Sku, l.ReceivedQuantity, l.InvoicedQuantity, l.UnitCost,
                    Money.Round((l.ReceivedQuantity - l.InvoicedQuantity) * l.UnitCost))))
            .ToList();
    }

    private Task PostInvoiceAsync(BranchScope scope, SupplierInvoice invoice, PurchaseOrder order, CancellationToken ct) =>
        ledger.PostAsync(scope, "SupplierInvoice", invoice.Id.ToString(), $"Supplier invoice {invoice.SupplierReference} ({order.OrderNumber})",
        [
            LedgerLine.Dr(AccountKeys.GoodsReceivedNotInvoiced, invoice.ReceivedValue),
            LedgerLine.Dr(AccountKeys.PurchasePriceVariance, invoice.Total - invoice.ReceivedValue),
            LedgerLine.Cr(AccountKeys.AccountsPayable, invoice.Total),
        ], ct);

    private async Task<PurchaseOrderDto> MutateOrderAsync(Guid branchId, Guid orderId, Guid stamp, Action<PurchaseOrder, BranchScope> action, CancellationToken ct)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var order = await db.PurchaseOrders.Include(o => o.Lines).GetOwnedAsync(branchId, orderId, "Purchase order", ct);
        BranchOperations.EnsureStamp(order, stamp, "purchase order");
        action(order, scope);
        await db.SaveChangesAsync(ct);
        return await GetOrderAsync(branchId, order.Id, ct);
    }

    private async Task RequireItemsAsync(Guid branchId, IEnumerable<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        var found = await db.InventoryItems.CountAsync(i => i.BranchId == branchId && i.IsActive && ids.Contains(i.Id), ct);
        if (found != ids.Count) throw new ValidationAppException("Lines", "One or more items do not exist in this branch or are inactive.");
    }

    private IQueryable<PurchaseOrder> OrdersQuery() =>
        db.PurchaseOrders.AsNoTracking().Include(o => o.Supplier).Include(o => o.Lines).ThenInclude(l => l.Item);

    private async Task<RequisitionDto> GetRequisitionAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.PurchaseRequisitions.AsNoTracking().Include(r => r.Lines).ThenInclude(l => l.Item).SingleAsync(r => r.Id == id, ct));

    private async Task<SupplierInvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.SupplierInvoices.AsNoTracking().Include(i => i.Supplier).Include(i => i.Lines).SingleAsync(i => i.Id == id, ct));

    private static RequisitionDto ToDto(PurchaseRequisition r) => new(r.Id, r.RequisitionNumber, r.Status.ToString(), r.RequestedByUserId, r.DecidedByUserId,
        r.RejectionReason, r.Notes, r.PurchaseOrderId,
        r.Lines.Select(l => new RequisitionLineDto(l.ItemId, l.Item?.Sku ?? string.Empty, l.Item?.Name ?? string.Empty, l.Quantity, l.EstimatedUnitCost)).ToList(),
        r.ConcurrencyStamp);

    private static PurchaseOrderDto ToDto(PurchaseOrder o) => new(o.Id, o.OrderNumber, o.SupplierId, o.Supplier?.Name ?? string.Empty, o.DeliveryLocationId,
        o.OrderDate, o.Status.ToString(), o.Total, o.OrderedByUserId, o.ApprovedByUserId, o.RequisitionId,
        o.Lines.Select(l => new PurchaseOrderLineDto(l.Id, l.ItemId, l.Item?.Sku ?? string.Empty, l.Item?.Name ?? string.Empty, l.Quantity, l.UnitCost,
            l.ReceivedQuantity, l.InvoicedQuantity)).ToList(), o.ConcurrencyStamp);

    private static GoodsReceiptDto ToDto(GoodsReceipt g) => new(g.Id, g.ReceiptNumber, g.PurchaseOrderId, g.BusinessDate, g.DeliveryNote, g.Total,
        g.Lines.Select(l => new GoodsReceiptLineDto(l.PurchaseOrderLineId, l.ItemId, l.Quantity, l.UnitCost)).ToList());

    private static SupplierInvoiceDto ToDto(SupplierInvoice i) => new(i.Id, i.InvoiceNumber, i.SupplierId, i.Supplier?.Name ?? string.Empty, i.PurchaseOrderId,
        i.SupplierReference, i.InvoiceDate, i.DueDate, i.Status.ToString(), i.Total, i.ReceivedValue, i.AmountPaid, i.Outstanding, i.DiscrepancyNotes,
        i.Lines.Select(l => new SupplierInvoiceLineDto(l.PurchaseOrderLineId, l.ItemId, l.Quantity, l.UnitPrice, l.Amount)).ToList(), i.ConcurrencyStamp);

    private static SupplierPaymentDto ToDto(SupplierPayment p) => new(p.Id, p.PaymentNumber, p.SupplierInvoiceId, p.Amount, p.Tender.ToString(), p.Reference, p.BusinessDate);
}
