using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Billing;
using HMS.Contracts.Common;
using HMS.Contracts.Rates;
using HMS.Contracts.Reservations;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>A guest's folio: charges, payments and balance; post services, take payments and request refunds.</summary>
public sealed partial class FolioPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public static readonly string[] Tenders = ["Cash", "PosCard", "BankTransfer"];

    public override string Title => "Folios and payments";

    [ObservableProperty] private string? _reservationNumber;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostChargeCommand), nameof(RecordPaymentCommand), nameof(RequestRefundCommand))]
    private FolioDto? _folio;

    public ObservableCollection<ServiceItemDto> ServiceItems { get; } = [];
    [ObservableProperty] private ServiceItemDto? _selectedService;
    [ObservableProperty] private decimal _quantity = 1;

    [ObservableProperty] private string _tender = "Cash";
    [ObservableProperty] private decimal? _paymentAmount;
    [ObservableProperty] private string? _paymentReference;
    [ObservableProperty] private decimal? _refundAmount;
    [ObservableProperty] private string? _refundReason;

    /// <summary>Kept while the same payment is retried, so a lost response never records it twice.</summary>
    private string _paymentKey = Guid.NewGuid().ToString();

    public bool CanPost => session.HasPermission(PermissionNames.FoliosPost);
    public bool CanPay => session.HasPermission(PermissionNames.PaymentsRecord);
    public bool CanRefund => session.HasPermission(PermissionNames.RefundsRequest);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        if (ServiceItems.Count == 0)
            FrontDeskPageViewModel.Replace(ServiceItems, (await api.GetAsync<List<ServiceItemDto>>(api.BranchPath("service-items"))).Where(s => s.IsActive));
        if (Folio is not null) Folio = await api.GetAsync<FolioDto>(api.BranchPath($"folios/{Folio.Id}"));
    });

    public async Task OpenAsync(Guid folioId)
    {
        await LoadAsync();
        await RunAsync(async () => Folio = await api.GetAsync<FolioDto>(api.BranchPath($"folios/{folioId}")));
        PaymentAmount = Folio?.Balance > 0 ? Folio.Balance : null;
    }

    [RelayCommand]
    private async Task FindAsync()
    {
        if (string.IsNullOrWhiteSpace(ReservationNumber)) { ErrorMessage = "Enter a reservation number or guest name."; return; }
        await RunAsync(async () =>
        {
            var match = (await api.GetAsync<PagedResult<ReservationListItemDto>>(Query.Build(api.BranchPath("reservations"),
                ("search", ReservationNumber.Trim()), ("pageSize", 2)))).Items;
            if (match.Count != 1) throw new ApiException(System.Net.HttpStatusCode.NotFound,
                ApiError.Create("NOT_FOUND", match.Count == 0 ? "No reservation matches." : "More than one reservation matches. Be more specific."));
            Folio = await api.GetAsync<FolioDto>(api.BranchPath($"reservations/{match[0].Id}/folio"));
        });
        PaymentAmount = Folio?.Balance > 0 ? Folio.Balance : null;
    }

    private bool FolioOpen() => Folio is { Status: "Open" } && !IsBusy;

    [RelayCommand(CanExecute = nameof(FolioOpen))]
    private Task PostChargeAsync()
    {
        if (SelectedService is null || Quantity <= 0) { ErrorMessage = "Pick a service and a quantity above zero."; return Task.CompletedTask; }
        var service = SelectedService;
        return RunAsync(async () => Folio = await api.PostAsync<FolioDto>(api.BranchPath($"folios/{Folio!.Id}/charges"),
            new PostChargeRequest(service.Id, null, Quantity, null), Guid.NewGuid().ToString()), $"{service.Name} posted.");
    }

    partial void OnTenderChanged(string value) => _paymentKey = Guid.NewGuid().ToString();
    partial void OnPaymentAmountChanged(decimal? value) => _paymentKey = Guid.NewGuid().ToString();

    [RelayCommand(CanExecute = nameof(FolioOpen))]
    private async Task RecordPaymentAsync()
    {
        if (PaymentAmount is not > 0) { ErrorMessage = "Enter an amount above zero."; return; }
        if (Tender != "Cash" && string.IsNullOrWhiteSpace(PaymentReference)) { ErrorMessage = "Enter the POS slip or transfer reference."; return; }
        var folio = Folio!;
        PaymentDto? payment = null;
        if (await RunAsync(async () => payment = await api.PostAsync<PaymentDto>(api.BranchPath($"folios/{folio.Id}/payments"),
                new RecordPaymentRequest(Tender, PaymentAmount.Value, string.IsNullOrWhiteSpace(PaymentReference) ? null : PaymentReference.Trim()), _paymentKey)))
        {
            _paymentKey = Guid.NewGuid().ToString();
            PaymentReference = null;
            Folio = await api.GetAsync<FolioDto>(api.BranchPath($"folios/{folio.Id}"));
            PaymentAmount = Folio.Balance > 0 ? Folio.Balance : null;
            StatusMessage = payment!.Status == "Confirmed"
                ? $"Payment of {payment.Amount:N2} recorded. Receipt {payment.ReceiptNumber}."
                : $"Payment of {payment.Amount:N2} recorded and waiting for verification.";
        }
    }

    [RelayCommand(CanExecute = nameof(FolioOpen))]
    private async Task RequestRefundAsync()
    {
        if (RefundAmount is not > 0 || string.IsNullOrWhiteSpace(RefundReason)) { ErrorMessage = "Enter the refund amount and a reason."; return; }
        if (!dialogs.Confirm($"Request a refund of {RefundAmount:N2} for {Folio!.GuestName}? A manager must approve it.")) return;
        var folio = Folio;
        if (await RunAsync(() => api.PostAsync<RefundDto>(api.BranchPath($"folios/{folio.Id}/refunds"),
                new RequestRefundRequest(null, RefundAmount.Value, Tender, RefundReason.Trim())), "Refund requested for approval."))
        {
            RefundAmount = null;
            RefundReason = null;
            Folio = await api.GetAsync<FolioDto>(api.BranchPath($"folios/{folio.Id}"));
        }
    }
}
