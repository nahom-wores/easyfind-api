using System.Text.Json;
using EasyFind.Api.Services.IServices;

namespace EasyFind.IntegrationTests;

// Stands in for Gemini. By default every call answers "fake reply".
//
// A test can script it to request a tool instead, then read back exactly what
// the tool returned — that drives the real controller, handler, agent loop and
// tool, with the signed-in user coming from the request as in production.
public class FakeChatModel : IChatModel
{
    private readonly Queue<ChatReply> _script = new();
    private readonly List<IReadOnlyList<ChatMessage>> _histories = [];

    public Task<ChatReply> SendAsync(string systemPrompt, IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        _histories.Add(history.ToList());
        return Task.FromResult(_script.TryDequeue(out var reply)
            ? reply
            : new ChatReply("fake reply", [], 0, 0));
    }

    // Next turn: the model asks for this tool once, then gives a final answer.
    public void ScriptToolCall(string toolName, object arguments)
    {
        _script.Clear();
        _histories.Clear();
        _script.Enqueue(new ChatReply(null,
            [new ToolCall("call-1", toolName, JsonSerializer.SerializeToElement(arguments), null)], 0, 0));
        _script.Enqueue(new ChatReply("done", [], 0, 0));
    }

    // What the tool returned on the scripted turn, as the model would see it.
    public JsonElement LastToolResult()
    {
        var result = _histories
            .SelectMany(h => h)
            .SelectMany(m => m.ToolResults ?? [])
            .LastOrDefault()
            ?? throw new InvalidOperationException("No tool result was sent to the model.");

        return JsonSerializer.SerializeToElement(result.Content);
    }
}
