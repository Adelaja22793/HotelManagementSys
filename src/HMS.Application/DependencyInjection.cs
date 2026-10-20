using HMS.Application.Abstractions;
using HMS.Application.Auditing;
using HMS.Application.Branches;
using HMS.Application.Billing;
using HMS.Application.Cashier;
using HMS.Application.Common;
using HMS.Application.FrontDesk;
using HMS.Application.Guests;
using HMS.Application.Housekeeping;
using HMS.Application.Rates;
using HMS.Application.Reservations;
using HMS.Application.Rooms;
using HMS.Application.Security;
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
        return services;
    }
}
