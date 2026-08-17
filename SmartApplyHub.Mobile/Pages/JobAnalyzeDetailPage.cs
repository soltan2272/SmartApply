using SmartApplyHub.Mobile.ViewModels;

namespace SmartApplyHub.Mobile.Pages;

/// <summary>
/// Separate type for Shell detail navigation from Search.
/// MAUI cannot RegisterRoute the same page type already used as ShellContent.
/// </summary>
public class JobAnalyzeDetailPage : JobAnalyzePage
{
    public JobAnalyzeDetailPage(JobAnalyzeViewModel vm) : base(vm)
    {
    }
}
