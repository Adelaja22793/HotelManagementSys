using HMS.Application.Abstractions;
using HMS.Application.Documents;
using HMS.Application.Notifications;
using HMS.Application.SecurityOps;
using HMS.Application.PublicBooking;
using HMS.Infrastructure.Documents;
using HMS.Infrastructure.Payments;
using HMS.Infrastructure.Notifications;
using HMS.Infrastructure.Identity;
using HMS.Infrastructure.Persistence;
using HMS.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));

        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<HmsDbContext>((sp, options) =>
        {
            // Resolved lazily so hosts and tests can override configuration after registration.
            var config = sp.GetRequiredService<IConfiguration>();
            var provider = config[$"{DatabaseOptions.SectionName}:Provider"] ?? "SqlServer";
            var connectionString = config.GetConnectionString("HotelManagementDb");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:HotelManagementDb is not configured.");

            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString);
            else
                // No automatic retry strategy: services use explicit transactions, and clients retry
                // safely with idempotency keys instead.
                options.UseSqlServer(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditingInterceptor>());
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<HmsDbContext>());

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<HmsDbContext>();

        services.AddSingleton<TokenService>();
        services.AddScoped<IIdentityService, IdentityService>();

        // Documents: private storage, malware scanning and signature keys
        services.Configure<DocumentOptions>(configuration.GetSection(DocumentOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<PublicBookingOptions>(configuration.GetSection(PublicBookingOptions.SectionName));
        services.AddSingleton<IDocumentStorage, FileSystemDocumentStorage>();
        services.AddSingleton<IDocumentSigner, HmacDocumentSigner>();
        services.AddSingleton<ClamAvScanner>();
        services.AddSingleton<NoMalwareScanner>();
        services.AddSingleton<IMalwareScanner, ConfiguredMalwareScanner>();

        // Notification providers. Channels without a provider fail visibly in the outbox instead of silently.
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        // Providers are chosen from configuration at send time (Email:Mode, Sms:Provider).
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<PickupEmailSender>();
        services.AddHttpClient<TermiiSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<INotificationSender, ConfiguredEmailSender>();
        services.AddScoped<INotificationSender, ConfiguredSmsSender>();

        // Online payments for the public booking website.
        services.Configure<PaystackOptions>(configuration.GetSection(PaystackOptions.SectionName));
        services.AddHttpClient<PaystackGateway>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<SandboxPaymentGateway>();
        services.AddScoped<IPaymentGateway, ConfiguredPaymentGateway>();
        return services;
    }
}
