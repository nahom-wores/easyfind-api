using Asp.Versioning;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;


[Route("api/v{version:apiVersion}/admin/stats")]
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
public class AdminStatsController(IAdminStatsService statsService) : ApiControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse>> GetOverview(CancellationToken ct)
        => HandleResult(await statsService.GetOverviewAsync(ct));
}