namespace EasyFind.Api.Services.IServices;

public enum ChatRole
{
    User,
    Assistant
}

public record ChatMessage(ChatRole Role, string Text);

public record ChatReply(string Text, int InputTokens, int OutputTokens);

public interface IChatModel
{
    Task<ChatReply> SendAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, CancellationToken ct);
}