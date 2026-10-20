using System.Windows;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
