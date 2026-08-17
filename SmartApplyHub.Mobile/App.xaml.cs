using SmartApplyHub.Mobile.Pages;
using SmartApplyHub.Mobile.Services;

namespace SmartApplyHub.Mobile;

public partial class App : Application
{
    private readonly AuthService _auth;
    private readonly IServiceProvider _services;

    public App(AuthService auth, IServiceProvider services)
    {
        InitializeComponent();
        _auth = auth;
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Handler / MauiContext are not available yet during CreateWindow.
        // Always resolve startup pages from the injected service provider.
        if (_auth.IsLoggedIn)
            return new Window(new AppShell());

        var loginPage = _services.GetRequiredService<LoginPage>();
        return new Window(new NavigationPage(loginPage)
        {
            BarBackgroundColor = Color.FromArgb("#4F46E5"),
            BarTextColor = Colors.White
        });
    }
}
