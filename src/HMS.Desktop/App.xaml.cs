using System.IO;
using System.Net.Http;
using System.Windows;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;
using HMS.Desktop.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HMS.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureAppConfiguration(config => config.AddJsonFile("appsettings.json", optional: false))
            .ConfigureServices((context, services) =>
            {
                var baseUrl = context.Configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");
                if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Api:BaseUrl must use HTTPS.");

                services.AddSingleton<SessionState>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddHttpClient<ApiClient>(client =>
                {
                    client.BaseAddress = new Uri(baseUrl);
                    client.Timeout = TimeSpan.FromSeconds(context.Configuration.GetValue("Api:TimeoutSeconds", 30));
                });
                services.AddSingleton(context.Configuration.GetSection("Updates").Get<DesktopUpdateOptions>() ?? new DesktopUpdateOptions());
                services.AddHttpClient<UpdateService>(client => client.Timeout = TimeSpan.FromMinutes(10));
                services.AddTransient<LoginViewModel>();
                services.AddSingleton<NavigationService>(sp => new NavigationService(type => (PageViewModel)sp.GetRequiredService(type)));
                foreach (var page in typeof(PageViewModel).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(PageViewModel)) && !t.IsAbstract))
                    services.AddTransient(page);
                services.AddTransient<ShellViewModel>();
                services.AddTransient<LoginWindow>();
                services.AddTransient<MainWindow>();
            })
            .Build();

        await _host.StartAsync();
        ShowLogin();
    }

    private void ShowLogin()
    {
        var login = _host!.Services.GetRequiredService<LoginWindow>();
        var vm = (LoginViewModel)login.DataContext;
        vm.Completed += (_, _) =>
        {
            ShowShell();
            login.Close();
        };
        login.Closed += (_, _) =>
        {
            if (_host.Services.GetRequiredService<SessionState>().Branch is null) Shutdown();
        };
        login.Show();
    }

    private void ShowShell()
    {
        var shell = _host!.Services.GetRequiredService<MainWindow>();
        var vm = (ShellViewModel)shell.DataContext;
        vm.SignedOut += (_, _) =>
        {
            ShowLogin();
            shell.Close();
        };
        shell.Closed += (_, _) =>
        {
            if (_host.Services.GetRequiredService<SessionState>().User is not null) Shutdown();
        };
        MainWindow = shell;
        shell.Show();
        _ = vm.StartAsync();
        _ = OfferUpdateAsync();
    }

    /// <summary>Checks for a signed update in the background and offers a restart once it is staged.</summary>
    private async Task OfferUpdateAsync()
    {
        try
        {
            var manifest = await _host!.Services.GetRequiredService<UpdateService>().CheckAndStageAsync(CancellationToken.None);
            if (manifest is null) return;
            var dialogs = _host.Services.GetRequiredService<IDialogService>();
            if (dialogs.Confirm($"HMS {manifest.Version} is ready. Restart now to use it?\n\nUnsaved work in open windows will be lost."))
            {
                UpdateService.ActivateAndRestart(manifest);
                Shutdown();
            }
        }
        catch (Exception ex) when (ex is HMS.Updater.UpdateRejectedException or HttpRequestException or IOException or TaskCanceledException)
        {
            // An update problem never blocks work; it is retried on the next start.
            System.Diagnostics.Trace.TraceWarning($"Update check failed: {ex.Message}");
        }
    }

    // PasswordBox cannot be bound, so a task form's password field is copied to its value as the user types.
    private void FormPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox { DataContext: HMS.Desktop.ViewModels.Setup.FormField field } box) field.Text = box.Password;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
