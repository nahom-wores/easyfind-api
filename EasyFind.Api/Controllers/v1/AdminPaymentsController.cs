using Asp.Versioning;
using EasyFind.Api.Features.Admin.Queries;
using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/admin/payments")]
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
public class AdminPaymentsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetPayments(
        [FromQuery] AdminPaymentFilterDto filter,
        [FromServices] ListPaymentsHandler handler,
        CancellationToken ct)
    {
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 100) filter.PageSize = 20;
        return HandleResult(await handler.HandleAsync(new ListPaymentsQuery(filter), ct));
    }
}
