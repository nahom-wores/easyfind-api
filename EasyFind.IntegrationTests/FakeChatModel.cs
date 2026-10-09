using EasyFind.Api.Services.IServices;

namespace EasyFind.IntegrationTests;

public class FakeChatModel : IChatModel
{
    public Task<ChatReply> SendAsync(string systemPrompt, IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
        => Task.FromResult(new ChatReply("fake reply", [], 0, 0));
}