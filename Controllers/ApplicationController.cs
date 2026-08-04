using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Models;
using JobApplicationBot.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers;

[Authorize]
public class ApplicationController : Controller
{
    private readonly IApplicationTrackingService _tracking;

    public ApplicationController(IApplicationTrackingService tracking)
    {
        _tracking = tracking;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated user has no NameIdentifier claim.");

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        if (page < 1) page = 1;

        var userId = CurrentUserId;
        var (apps, totalCount) = await _tracking.GetPagedApplicationsAsync(
            userId, page, ApplicationDashboardViewModel.PageSize, ct);
        var due = await _tracking.GetDueFollowUpsAsync(userId, ct);
        var counts = await _tracking.GetStatusCountsAsync(userId, ct);

        var vm = new ApplicationDashboardViewModel
        {
            Applications = apps,
            DueFollowUps = due,
            CurrentPage = page,
            TotalCount = totalCount,
            TotalApplications = counts.Total,
            TotalSent = counts.Sent,
            TotalFailed = counts.Failed,
            TotalQueued = counts.Queued,
            PipelineInterviewing = counts.Interviewing,
            PipelineOffer = counts.Offer,
            PipelineRejected = counts.Rejected
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Dispatch(int id, CancellationToken ct)
    {
        var dispatch = await _tracking.GetDispatchAsync(CurrentUserId, id, ct);
        if (dispatch == null)
            return NotFound();

        return View(dispatch);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduleFollowUp(int id, DateTime? nextFollowUpAt, int page = 1, CancellationToken ct = default)
    {
        try
        {
            await _tracking.ScheduleFollowUpAsync(CurrentUserId, id, nextFollowUpAt?.ToUniversalTime(), ct);
            TempData["ApplicationsSuccess"] = "Follow-up reminder updated.";
        }
        catch (Exception ex)
        {
            TempData["ApplicationsError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendFollowUp(int id, int page = 1, CancellationToken ct = default)
    {
        try
        {
            await _tracking.SendFollowUpAsync(CurrentUserId, id, ct);
            TempData["ApplicationsSuccess"] = "Follow-up email sent successfully.";
        }
        catch (Exception ex)
        {
            TempData["ApplicationsError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePipeline(int id, string pipelineStatus, int page = 1, CancellationToken ct = default)
    {
        try
        {
            await _tracking.UpdatePipelineStatusAsync(CurrentUserId, id, pipelineStatus, ct);
            TempData["ApplicationsSuccess"] = "Pipeline status updated.";
        }
        catch (Exception ex)
        {
            TempData["ApplicationsError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { page });
    }
}
