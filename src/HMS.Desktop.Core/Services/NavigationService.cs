using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Services;

/// <summary>Opens screens in the main window. Screens use it to jump to a related record, such as a stay's folio.</summary>
public sealed class NavigationService(Func<Type, PageViewModel> create)
{
    public PageViewModel? Current { get; private set; }

    public event EventHandler<PageViewModel>? Navigated;

    /// <summary>Opens a screen. <paramref name="prepare"/> replaces the normal load, for example to open one record.</summary>
    public async Task<TPage> OpenAsync<TPage>(Func<TPage, Task>? prepare = null) where TPage : PageViewModel
    {
        var page = (TPage)Show(typeof(TPage));
        await (prepare is null ? page.LoadAsync() : prepare(page));
        return page;
    }

    public async Task OpenAsync(Type pageType) => await Show(pageType).LoadAsync();

    private PageViewModel Show(Type pageType)
    {
        var page = create(pageType);
        Current = page;
        Navigated?.Invoke(this, page);
        return page;
    }
}
