using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile.Pages;

public partial class JobAnalyzePage : ContentPage
{
    public JobAnalyzePage(JobAnalyzeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
