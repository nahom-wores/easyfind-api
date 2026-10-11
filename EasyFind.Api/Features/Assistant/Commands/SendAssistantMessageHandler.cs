using EasyFind.Api.Data;
using EasyFind.Api.Models.Dto.Assistant;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Prompts;
using EasyFind.Api.Services.Assistant;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Features.Assistant.Commands;

public sealed record SendAssistantMessageCommand(string UserId, AssistantChatRequest Chat);

// A command rather than a query: nothing is written, but every call spends
// Gemini quota and may run tools.
public class SendAssistantMessageHandler(
    ApplicationDbContext db,
    AssistantAgent agent,
    IOptions<SubscriptionOptions> subscription)
{
    public async Task<Result<AssistantChatResponse>> HandleAsync(SendAssistantMessageCommand command, CancellationToken ct = default)
    {
        var (userId, chat) = command;

        var tier = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SubscriptionTier)
            .FirstOrDefaultAsync(ct);

        // App format -> our internal format
        var history = chat.Messages
            .Select(m => new ChatMessage(
                m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                m.Text))
            .ToList();

        var systemPrompt = AssistantPrompts.BuildSystem(subscription.Value, tier.ToString());
        var replyText = await agent.RunAsync(systemPrompt, history, ct);

        return Result<AssistantChatResponse>.Success(new AssistantChatResponse(replyText));
    }
}
