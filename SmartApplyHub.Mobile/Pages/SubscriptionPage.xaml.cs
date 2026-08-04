using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile.Pages;

public partial class SubscriptionPage : ContentPage
{
    private readonly SubscriptionViewModel _vm;

    public SubscriptionPage(SubscriptionViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadCommand.ExecuteAsync(null);
    }
}
