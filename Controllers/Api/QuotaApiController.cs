using System.Security.Claims;
using JobApplicationBot.Models.Api;
using JobApplicationBot.Services.Billing;
using JobApplicationBot.Services.Quota;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers.Api;

[ApiController]
[Route("api/quota")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class QuotaApiController : ControllerBase
{
    private readonly IAiQuotaService _quota;
    private readonly ISubscriptionService _subscriptions;

    public QuotaApiController(IAiQuotaService quota, ISubscriptionService subscriptions)
    {
        _quota = quota;
        _subscriptions = subscriptions;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var status = await _quota.GetStatusAsync(UserId, ct);
        var plan = await _subscriptions.GetPlanAsync(UserId, ct);

        return Ok(new QuotaStatusDto
        {
            Used = status.Used,
            Limit = status.Limit,
            Remaining = status.Remaining,
            Plan = plan
        });
    }

    [HttpGet("plans")]
    [AllowAnonymous]
    public IActionResult Plans()
    {
        var plans = _subscriptions.GetPublicPlans();
        var dtos = plans.Select(p => new PlanInfoDto
        {
            Plan = p.Plan,
            AiPerMonth = p.AiPerMonth,
            BulkMaxRecipients = p.BulkMaxRecipients,
            MonthlyPriceUsd = p.MonthlyPriceUsd
        }).ToList();

        return Ok(dtos);
    }
}
