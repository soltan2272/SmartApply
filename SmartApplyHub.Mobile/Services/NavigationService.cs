namespace SmartApplyHub.Mobile.Services;

/// <summary>
/// Wraps classic (NavigationPage-based) navigation for pages that live outside
/// the AppShell, such as Login and Register. Shell.Current is null while those
/// pages are shown, so Shell.GoToAsync cannot be used there.
/// </summary>
public class NavigationService
{
    private readonly IServiceProvider _services;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public Task PushAsync<TPage>() where TPage : Page
    {
        var page = _services.GetRequiredService<TPage>();
        var navigation = Application.Current?.Windows[0].Page?.Navigation
            ?? throw new InvalidOperationException("No active page to navigate from.");
        return navigation.PushAsync(page);
    }

    public Task PopAsync()
    {
        var navigation = Application.Current?.Windows[0].Page?.Navigation
            ?? throw new InvalidOperationException("No active page to navigate from.");
        return navigation.PopAsync();
    }
}
