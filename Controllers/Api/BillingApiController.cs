using System.Security.Claims;
using JobApplicationBot.Models.Api;
using JobApplicationBot.Services.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class BillingApiController : ControllerBase
{
    private readonly IAdminRequestService _adminRequests;

    public BillingApiController(IAdminRequestService adminRequests)
    {
        _adminRequests = adminRequests;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost("request-subscription")]
    public async Task<IActionResult> RequestSubscription([FromBody] SubscriptionRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _adminRequests.RequestSubscriptionAsync(UserId, request.Plan, request.Note, ct);
            return Ok(new RequestStatusDto
            {
                Id = result.Id,
                Status = result.Status,
                RequestedPlan = result.RequestedPlan,
                CreatedAt = result.CreatedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Error = ex.Message });
        }
    }

    [HttpPost("request-token-reset")]
    public async Task<IActionResult> RequestTokenReset([FromBody] TokenResetRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = await _adminRequests.RequestTokenResetAsync(UserId, request.Note, ct);
            return Ok(new RequestStatusDto
            {
                Id = result.Id,
                Status = result.Status,
                CreatedAt = result.CreatedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse { Error = ex.Message });
        }
    }

    [HttpGet("pending-subscription")]
    public async Task<IActionResult> PendingSubscription(CancellationToken ct)
    {
        var pending = await _adminRequests.GetUserPendingSubscriptionAsync(UserId, ct);
        if (pending == null)
            return Ok(new { HasPending = false });

        return Ok(new
        {
            HasPending = true,
            Request = new RequestStatusDto
            {
                Id = pending.Id,
                Status = pending.Status,
                RequestedPlan = pending.RequestedPlan,
                CreatedAt = pending.CreatedAt
            }
        });
    }

    [HttpGet("pending-token-reset")]
    public async Task<IActionResult> PendingTokenReset(CancellationToken ct)
    {
        var pending = await _adminRequests.GetUserPendingTokenResetAsync(UserId, ct);
        if (pending == null)
            return Ok(new { HasPending = false });

        return Ok(new
        {
            HasPending = true,
            Request = new RequestStatusDto
            {
                Id = pending.Id,
                Status = pending.Status,
                CreatedAt = pending.CreatedAt
            }
        });
    }
}
