using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Views.Pages;

public partial class SetupPage : UserControl
{
    public SetupPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SetupPageViewModel old) old.Columns.CollectionChanged -= OnColumnsChanged;
            if (e.NewValue is SetupPageViewModel vm)
            {
                vm.Columns.CollectionChanged += OnColumnsChanged;
                BuildColumns(vm);
            }
        };
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is SetupPageViewModel vm) BuildColumns(vm);
    }

    // The columns depend on what is being set up, so they are made here from the view model's headers.
    private void BuildColumns(SetupPageViewModel vm)
    {
        RecordsGrid.Columns.Clear();
        for (var i = 0; i < vm.Columns.Count; i++)
        {
            RecordsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = vm.Columns[i],
                Binding = new Binding($"Cells[{i}]"),
                Width = i == 1 ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            });
        }
    }
}
