using SmartApplyHub.Mobile.Pages;

namespace SmartApplyHub.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("JobAnalyze", typeof(JobAnalyzePage));
    }
}
