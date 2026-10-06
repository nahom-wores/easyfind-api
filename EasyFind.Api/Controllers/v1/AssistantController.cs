using Asp.Versioning;
using EasyFind.Api.Models.Dto.Assistant;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class AssistantController : ApiControllerBase
{
    private const string SystemPrompt =
        "You are Yisru's assistant. Yisru helps Ethiopians find visa-sponsored jobs and scholarships abroad. Answer briefly.";

    [HttpPost("chat")]
    public async Task<ActionResult<AssistantChatResponse>> Chat(
        AssistantChatRequest request,
        [FromServices] IChatModel model,
        CancellationToken ct)
    {
        // App format -> our internal format
        var history = request.Messages
            .Select(m => new ChatMessage(
                m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                m.Text))
            .ToList();

        var reply = await model.SendAsync(SystemPrompt, history, ct);

        return Ok(new AssistantChatResponse(reply.Text));
    }
}