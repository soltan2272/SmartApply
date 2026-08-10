using SmartApplyHub.Mobile.Pages;

namespace SmartApplyHub.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail route used when tapping a search result (pushes on top of shell).
        Routing.RegisterRoute("JobAnalyzeDetail", typeof(JobAnalyzePage));
    }
}
