using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Services.Admin;
using JobApplicationBot.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IAdminRequestService _admin;
    private readonly ISubscriptionService _subscriptions;

    public AdminController(IAdminRequestService admin, ISubscriptionService subscriptions)
    {
        _admin = admin;
        _subscriptions = subscriptions;
    }

    private string AdminId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Not authenticated.");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var stats = await _admin.GetStatsAsync(ct);
        return View(stats);
    }

    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var users = await _admin.GetUsersAsync(ct);
        ViewBag.Plans = _subscriptions.GetPublicPlans();
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> SubscriptionRequests(CancellationToken ct)
    {
        var requests = await _admin.GetPendingSubscriptionsAsync(ct);
        return View(requests);
    }

    [HttpGet]
    public async Task<IActionResult> TokenResetRequests(CancellationToken ct)
    {
        var requests = await _admin.GetPendingTokenResetsAsync(ct);
        return View(requests);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveSubscription(int id, CancellationToken ct)
    {
        try
        {
            await _admin.ApproveSubscriptionAsync(id, AdminId, ct);
            TempData["AdminSuccess"] = "Subscription request approved.";
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        return RedirectToAction(nameof(SubscriptionRequests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectSubscription(int id, CancellationToken ct)
    {
        try
        {
            await _admin.RejectSubscriptionAsync(id, AdminId, ct);
            TempData["AdminSuccess"] = "Subscription request rejected.";
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        return RedirectToAction(nameof(SubscriptionRequests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveTokenReset(int id, CancellationToken ct)
    {
        try
        {
            await _admin.ApproveTokenResetAsync(id, AdminId, ct);
            TempData["AdminSuccess"] = "Token reset approved.";
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        return RedirectToAction(nameof(TokenResetRequests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectTokenReset(int id, CancellationToken ct)
    {
        try
        {
            await _admin.RejectTokenResetAsync(id, AdminId, ct);
            TempData["AdminSuccess"] = "Token reset rejected.";
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        return RedirectToAction(nameof(TokenResetRequests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetUserTokens(string userId, CancellationToken ct)
    {
        try
        {
            await _admin.ResetUserTokensAsync(userId, ct);
            TempData["AdminSuccess"] = "User AI tokens reset for this month.";
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetUserPlan(string userId, string plan, CancellationToken ct)
    {
        try
        {
            await _admin.SetUserPlanAsync(userId, plan, ct);
            TempData["AdminSuccess"] = $"User plan set to {SubscriptionPlans.Normalize(plan)}.";
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        return RedirectToAction(nameof(Users));
    }
}
