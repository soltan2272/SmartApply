using SmartApplyHub.Mobile.Pages;

namespace SmartApplyHub.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Must use a distinct page type — JobAnalyzePage is already a ShellContent.
        Routing.RegisterRoute("JobAnalyzeDetail", typeof(JobAnalyzeDetailPage));
    }
}

