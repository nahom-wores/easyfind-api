using Asp.Versioning;
using EasyFind.Api.Features.Assistant.Commands;
using EasyFind.Api.Models.Dto.Assistant;
using EasyFind.Api.Models.Dto.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class AssistantController : ApiControllerBase
{
    [HttpPost("chat")]
    public async Task<ActionResult<ApiResponse>> Chat(
        AssistantChatRequest request,
        [FromServices] SendAssistantMessageHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new SendAssistantMessageCommand(UserId, request), ct);
        return HandleResult(result);
    }
}
