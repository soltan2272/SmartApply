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
[Route("api/[controller]")]
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

        var dtos = items.Select(a => new ApplicationDto
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
            SentAt = a.SentAt
        }).ToList();

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
}
