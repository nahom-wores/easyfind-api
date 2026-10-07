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

    public async Task<ChatReply> SendAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var o = options.Value;
        // Our neutral messages -> Gemini's format ("assistant" is called "model" in Gemini)
        var body = new GeminiRequest(
            SystemInstruction: new GeminiContent(null, [new GeminiPart(systemPrompt)]),
            Contents: history
                .Select(m => new GeminiContent(m.Role == ChatRole.User ? "user" : "model", [new GeminiPart(m.Text)]))
                .ToList());
        
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
        // Anything but STOP (SAFETY, MAX_TOKENS, RECITATION, ...) or no candidate at
        // all means the text below is empty or cut short. Without this line an empty
        // reply is indistinguishable from a blocked one.
        if (candidate?.FinishReason != "STOP")
            logger.LogWarning("Gemini reply did not finish normally: model={Model} finishReason={FinishReason}",
                o.Model, candidate?.FinishReason ?? "(no candidate)");

        var text = candidate?.Content?.Parts?.FirstOrDefault()?.Text ?? "";
        var inTokens = result?.UsageMetadata?.PromptTokenCount ?? 0;
        var outTokens = result?.UsageMetadata?.CandidatesTokenCount ?? 0;

        logger.LogInformation("Gemini call: model={Model} in={In} out={Out}", o.Model, inTokens, outTokens);

        return new ChatReply(text, inTokens, outTokens);
    }
}