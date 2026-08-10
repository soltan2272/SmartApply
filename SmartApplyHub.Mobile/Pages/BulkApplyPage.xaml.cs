using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile.Pages;

public partial class BulkApplyPage : ContentPage
{
    private readonly BulkApplyViewModel _vm;

    public BulkApplyPage(BulkApplyViewModel vm)
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
