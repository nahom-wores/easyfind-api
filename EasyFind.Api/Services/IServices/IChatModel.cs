using System.Text.Json;

namespace EasyFind.Api.Services.IServices;

public enum ChatRole
{
    User,
    Assistant
}

/// A tool we offer the model: name, description, and a JSON schema for its parameters.
public record ToolDefinition(string Name, string Description, object Parameters);

/// The model asking us to run a tool.
/// Signature is provider-specific (Gemini's thoughtSignature) and must be sent back unchanged.
public record ToolCall(string? Id, string Name, JsonElement Arguments, string? Signature);

/// Our answer to a ToolCall. Id and Name must match the call.
public record ToolResult(string? Id, string Name, object Content);

public record ChatMessage(
    ChatRole Role,
    string? Text = null,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    IReadOnlyList<ToolResult>? ToolResults = null);

/// Either Text (a final answer) or ToolCalls (the model wants tools run), sometimes both.
public record ChatReply(string? Text, IReadOnlyList<ToolCall> ToolCalls, int InputTokens, int OutputTokens);

public interface IChatModel
{
    Task<ChatReply> SendAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct);
}