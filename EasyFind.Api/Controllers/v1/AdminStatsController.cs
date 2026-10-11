using Asp.Versioning;
using EasyFind.Api.Features.Admin.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/admin/stats")]
[ApiController]
[ApiVersion("1.0")]
[Authorize(Policy = AppPolicies.AdminAccess)]
public class AdminStatsController : ApiControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse>> GetOverview(
        [FromServices] GetOverviewStatsHandler handler,
        CancellationToken ct)
        => HandleResult(await handler.HandleAsync(ct));
}
