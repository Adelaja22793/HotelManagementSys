using System.Windows;
using System.Windows.Input;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    // PasswordBox does not support binding by design; pass the value straight to the command.
    private void SignIn_Click(object sender, RoutedEventArgs e) => Execute();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Execute();
    }

    private void Execute()
    {
        if (_viewModel.LoginCommand.CanExecute(PasswordBox.Password))
            _viewModel.LoginCommand.Execute(PasswordBox.Password);
    }
}
