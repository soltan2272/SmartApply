using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Models;
using JobApplicationBot.Services.Admin;
using JobApplicationBot.Services.Billing;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers;

[Authorize]
public class BillingController : Controller
{
    private readonly ISubscriptionService _subscriptions;
    private readonly IAdminRequestService _requests;
    private readonly IAiQuotaService _quota;

    public BillingController(
        ISubscriptionService subscriptions,
        IAdminRequestService requests,
        IAiQuotaService quota)
    {
        _subscriptions = subscriptions;
        _requests = requests;
        _quota = quota;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Not authenticated.");

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentPlan = string.IsNullOrEmpty(userId)
            ? SubscriptionPlans.Free
            : await _subscriptions.GetPlanAsync(userId, ct);

        var quota = string.IsNullOrEmpty(userId)
            ? null
            : await _quota.GetStatusAsync(userId, ct);

        var pendingSub = string.IsNullOrEmpty(userId)
            ? null
            : await _requests.GetUserPendingSubscriptionAsync(userId, ct);

        var pendingToken = string.IsNullOrEmpty(userId)
            ? null
            : await _requests.GetUserPendingTokenResetAsync(userId, ct);

        var vm = new BillingPageViewModel
        {
            CurrentPlan = currentPlan,
            Plans = _subscriptions.GetPublicPlans().ToList(),
            StripeConfigured = false,
            QuotaUsed = quota?.Used ?? 0,
            QuotaLimit = quota?.Limit ?? 0,
            HasPendingSubscriptionRequest = pendingSub != null,
            PendingSubscriptionPlan = pendingSub?.RequestedPlan,
            HasPendingTokenResetRequest = pendingToken != null
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestSubscription(string plan, string? note, CancellationToken ct)
    {
        try
        {
            await _requests.RequestSubscriptionAsync(CurrentUserId, plan, note, ct);
            TempData["BillingSuccess"] = "Subscription request submitted. An admin will review it shortly.";
        }
        catch (Exception ex)
        {
            TempData["BillingError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestTokenReset(string? note, CancellationToken ct)
    {
        try
        {
            await _requests.RequestTokenResetAsync(CurrentUserId, note, ct);
            TempData["BillingSuccess"] = "Token reset request submitted. An admin will review it shortly.";
        }
        catch (Exception ex)
        {
            TempData["BillingError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
