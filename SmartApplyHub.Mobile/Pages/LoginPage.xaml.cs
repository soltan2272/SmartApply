using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
