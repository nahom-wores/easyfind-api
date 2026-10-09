using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services.Assistant;

public class AssistantAgent( IChatModel model,
    IEnumerable<IAssistantTool> tools,
    ILogger<AssistantAgent> logger)
{
    private const int MaxToolRounds = 3;
    private const string FallbackReply = "Sorry, I couldn't complete that request. Please try rephrasing your question.";

    public async Task<string> RunAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var toolsByName = tools.ToDictionary(t => t.Definition.Name);
        var definitions = toolsByName.Values.Select(t => t.Definition).ToList();

        // Copy: we append tool calls/results without touching the caller's list
        var conversation = new List<ChatMessage>(history);

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var reply = await model.SendAsync(systemPrompt, conversation, definitions, ct);

            // 1. No tool requests → this is the final answer
            if (reply.ToolCalls.Count == 0)
                return reply.Text ?? FallbackReply;

            // 2. Safety brake: the model keeps asking for tools
            if (round == MaxToolRounds)
                break;

            // 3. Record the model's request (with its signature) — exactly Part 2's "model" entry
            conversation.Add(new ChatMessage(ChatRole.Assistant, reply.Text, ToolCalls: reply.ToolCalls));

            // 4. Run each requested tool and record the results — Part 2's "functionResponse" entry
            var results = new List<ToolResult>();
            foreach (var call in reply.ToolCalls)
                results.Add(new ToolResult(call.Id, call.Name, await ExecuteToolAsync(call, toolsByName, ct)));

            conversation.Add(new ChatMessage(ChatRole.User, ToolResults: results));
        }

        logger.LogWarning("Assistant stopped after {Max} tool rounds", MaxToolRounds);
        return FallbackReply;
    }
    private async Task<object> ExecuteToolAsync(
        ToolCall call, Dictionary<string, IAssistantTool> toolsByName, CancellationToken ct)
    {
        // The model can name a tool that doesn't exist — never trust the name blindly
        if (!toolsByName.TryGetValue(call.Name, out var tool))
        {
            logger.LogWarning("Model requested unknown tool {Tool}", call.Name);
            return new { error = $"Unknown tool '{call.Name}'." };
        }

        try
        {
            logger.LogInformation("Running tool {Tool}", call.Name);
            return await tool.ExecuteAsync(call.Arguments, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Report the failure TO THE MODEL instead of crashing the request
            logger.LogError(ex, "Tool {Tool} failed", call.Name);
            return new { error = "The search failed. Tell the user to try again later." };
        }
    }
}