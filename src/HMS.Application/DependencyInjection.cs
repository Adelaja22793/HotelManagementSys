using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Auditing;
using HMS.Application.Branches;
using HMS.Application.Billing;
using HMS.Application.Cashier;
using HMS.Application.Common;
using HMS.Application.FrontDesk;
using HMS.Application.Guests;
using HMS.Application.Housekeeping;
using HMS.Application.Inventory;
using HMS.Application.Maintenance;
using HMS.Application.Purchasing;
using HMS.Application.Rates;
using HMS.Application.Reservations;
using HMS.Application.Restaurant;
using HMS.Application.Rooms;
using HMS.Application.Security;
using HMS.Application.Staff;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<IBranchAccessService, BranchAccessService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<INumberSequence, NumberSequence>();

        // Phase 2: core operations
        services.AddScoped<IRoomInventoryService, RoomInventoryService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IRateService, RateService>();
        services.AddScoped<IGuestService, GuestService>();
        services.AddScoped<IFolioService, FolioService>();
        services.AddScoped<ICashierService, CashierService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IFrontDeskService, FrontDeskService>();
        services.AddScoped<IHousekeepingService, HousekeepingService>();

        // Phase 3: operations and finance
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IAccountingService, AccountingService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPurchasingService, PurchasingService>();
        services.AddScoped<IRestaurantService, RestaurantService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IStaffService, StaffService>();
        return services;
    }
}
