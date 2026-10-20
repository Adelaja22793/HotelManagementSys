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
                services.AddTransient<LoginViewModel>();
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
