using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile.Pages;

public partial class RegisterPage : ContentPage
{
    public RegisterPage(RegisterViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
