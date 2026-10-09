using System.Text.Json;
using System.Text.Json.Serialization;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Services.IServices;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Services.Gemini;

public class GeminiClient(HttpClient http, IOptions<GeminiOptions> options,
    ILogger<GeminiClient> logger) : IChatModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };


    public async Task<ChatReply> SendAsync(string systemPrompt,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        var o = options.Value;

    var body = new GeminiRequest(
        SystemInstruction: new GeminiContent(null, [new GeminiPart(Text: systemPrompt)]),
        Contents: history.Select(ToGemini).ToList(),
        Tools: tools.Count == 0
            ? null
            : [new GeminiTool(tools.Select(t => new GeminiFunctionDeclaration(t.Name, t.Description, t.Parameters)).ToList())]);

    using var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{o.Model}:generateContent")
    {
        Content = JsonContent.Create(body, options: Json)
    };
    request.Headers.Add("x-goog-api-key", o.ApiKey);

    using var response = await http.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Gemini {(int)response.StatusCode}: {error}");
    }

    var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(Json, ct);

    var candidate = result?.Candidates?.FirstOrDefault();
    var parts = candidate?.Content?.Parts ?? [];
    var finishReason = candidate?.FinishReason ?? "NO_CANDIDATE";

    // Text parts → the answer. FunctionCall parts → tool requests (keep the signature!)
    var text = string.Concat(parts.Where(p => p.Text is not null).Select(p => p.Text));
    var toolCalls = parts
        .Where(p => p.FunctionCall is not null)
        .Select(p => new ToolCall(p.FunctionCall!.Id, p.FunctionCall.Name, p.FunctionCall.Args, p.ThoughtSignature))
        .ToList();

    var inTokens = result?.UsageMetadata?.PromptTokenCount ?? 0;
    var outTokens = result?.UsageMetadata?.CandidatesTokenCount ?? 0;

    logger.LogInformation("Gemini call: model={Model} in={In} out={Out} toolCalls={Calls}",
        o.Model, inTokens, outTokens, toolCalls.Count);
    if (finishReason != "STOP")
        logger.LogWarning("Gemini did not finish normally: {FinishReason}", finishReason);

    return new ChatReply(string.IsNullOrEmpty(text) ? null : text, toolCalls, inTokens, outTokens);
    }
    // Our neutral message → Gemini's format. Exactly what you built by hand in Part 2.
    private static GeminiContent ToGemini(ChatMessage m)
    {
        var parts = new List<GeminiPart>();

        if (!string.IsNullOrEmpty(m.Text))
            parts.Add(new GeminiPart(Text: m.Text));

        foreach (var call in m.ToolCalls ?? [])
            parts.Add(new GeminiPart(
                FunctionCall: new GeminiFunctionCall(call.Name, call.Arguments, call.Id),
                ThoughtSignature: call.Signature));

        foreach (var res in m.ToolResults ?? [])
            parts.Add(new GeminiPart(
                FunctionResponse: new GeminiFunctionResponse(res.Name, res.Content, res.Id)));

        return new GeminiContent(m.Role == ChatRole.User ? "user" : "model", parts);
    }
}