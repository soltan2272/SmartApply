using System.Security.Claims;
using JobApplicationBot.Data.Entities;
using JobApplicationBot.Models;
using JobApplicationBot.Models.Api;
using JobApplicationBot.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobApplicationBot.Controllers.Api;

[ApiController]
[Route("api/applications")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ApplicationsApiController : ControllerBase
{
    private readonly IApplicationTrackingService _tracking;

    public ApplicationsApiController(IApplicationTrackingService tracking)
    {
        _tracking = tracking;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        const int pageSize = ApplicationDashboardViewModel.PageSize;

        var (items, totalCount) = await _tracking.GetPagedApplicationsAsync(UserId, page, pageSize, ct);
        var counts = await _tracking.GetStatusCountsAsync(UserId, ct);

        var dtos = items.Select(a => ToDto(a)).ToList();

        var totalPages = totalCount <= 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PagedResult<ApplicationDto>
        {
            Items = dtos,
            CurrentPage = page,
            TotalCount = totalCount,
            TotalPages = totalPages,
            PageSize = pageSize
        });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var counts = await _tracking.GetStatusCountsAsync(UserId, ct);

        return Ok(new ApplicationStatsDto
        {
            TotalApplications = counts.Total,
            TotalSent = counts.Sent,
            TotalFailed = counts.Failed,
            TotalQueued = counts.Queued,
            PipelineInterviewing = counts.Interviewing,
            PipelineOffer = counts.Offer,
            PipelineRejected = counts.Rejected
        });
    }

    [HttpPut("{id:int}/pipeline")]
    public async Task<IActionResult> UpdatePipeline(int id, [FromBody] UpdatePipelineRequest request, CancellationToken ct)
    {
        if (!ApplicationPipelineStatus.IsValid(request.PipelineStatus))
            return BadRequest(new ApiErrorResponse { Error = $"Invalid pipeline status. Valid values: {string.Join(", ", ApplicationPipelineStatus.All)}" });

        var app = await _tracking.GetApplicationAsync(UserId, id, ct);
        if (app == null)
            return NotFound(new ApiErrorResponse { Error = "Application not found." });

        await _tracking.UpdatePipelineStatusAsync(UserId, id, request.PipelineStatus, ct);
        return Ok(new { Message = "Pipeline status updated." });
    }

    [HttpGet("due-follow-ups")]
    public async Task<IActionResult> DueFollowUps(CancellationToken ct)
    {
        var due = await _tracking.GetDueFollowUpsAsync(UserId, ct);
        return Ok(due.Select(ToDto).ToList());
    }

    [HttpPut("{id:int}/follow-up")]
    public async Task<IActionResult> ScheduleFollowUp(int id, [FromBody] ScheduleFollowUpRequest request, CancellationToken ct)
    {
        try
        {
            await _tracking.ScheduleFollowUpAsync(UserId, id, request.NextFollowUpAt?.ToUniversalTime(), ct);
            return Ok(new { Message = "Follow-up reminder updated." });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiErrorResponse { Error = ex.Message });
        }
    }

    [HttpPost("{id:int}/follow-up/send")]
    public async Task<IActionResult> SendFollowUp(int id, CancellationToken ct)
    {
        try
        {
            await _tracking.SendFollowUpAsync(UserId, id, ct);
            return Ok(new { Message = "Follow-up email sent successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiErrorResponse { Error = ex.Message });
        }
    }

    private static ApplicationDto ToDto(JobApplication a) => new()
    {
        Id = a.Id,
        JobTitle = a.JobTitle,
        CompanyName = a.CompanyName,
        RecipientEmail = a.RecipientEmail,
        Subject = a.Subject,
        Status = a.Status,
        PipelineStatus = a.PipelineStatus,
        Source = a.Source,
        MatchScore = a.MatchScore,
        CreatedAt = a.CreatedAt,
        SentAt = a.SentAt,
        NextFollowUpAt = a.NextFollowUpAt
    };
}
