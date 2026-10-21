using System.Diagnostics;
using System.Windows;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Views;

public partial class StartupWindow : Window
{
    private readonly StartupViewModel _viewModel;

    public StartupWindow(StartupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        viewModel.Started += (_, _) =>
        {
            if (IsVisible) DialogResult = true;
        };
        Loaded += async (_, _) =>
        {
            if (viewModel.IsSetupNeeded) HotelBox.Focus();
            else await viewModel.StartAsync();
        };
    }

    // PasswordBox does not support binding by design; pass the values straight to the view model.
    private async void Setup_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.SubmitSetupAsync(PasswordBox.Password, ConfirmBox.Password);

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsSetupNeeded) await _viewModel.SubmitSetupAsync(PasswordBox.Password, ConfirmBox.Password);
        else await _viewModel.StartAsync();
    }

    private void LocalDbLink_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(StartupViewModel.LocalDbDownloadUrl) { UseShellExecute = true });
}
