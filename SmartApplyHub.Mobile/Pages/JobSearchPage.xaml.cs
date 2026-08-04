using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile.Pages;

public partial class JobSearchPage : ContentPage
{
    public JobSearchPage(JobSearchViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
