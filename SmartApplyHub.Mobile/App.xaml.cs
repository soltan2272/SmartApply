using SmartApplyHub.Mobile.Pages;
using SmartApplyHub.Mobile.Services;
using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile;

public partial class App : Application
{
    private readonly AuthService _auth;

    public App(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        if (_auth.IsLoggedIn)
            return new Window(new AppShell());

        return new Window(new NavigationPage(
            new LoginPage(
                Handler?.MauiContext?.Services.GetRequiredService<LoginViewModel>()
                    ?? throw new InvalidOperationException("Cannot resolve LoginViewModel")))
        {
            BarBackgroundColor = Color.FromArgb("#512BD4"),
            BarTextColor = Colors.White
        });
    }
}
