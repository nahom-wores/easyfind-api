using System.Text.Json;

namespace EasyFind.Api.Services.Gemini;

internal record GeminiRequest(GeminiContent? SystemInstruction, List<GeminiContent> Contents, List<GeminiTool>? Tools);
internal record GeminiContent(string? Role, List<GeminiPart> Parts);

// A part is ONE of: text, a function call, or a function response
internal record GeminiPart(
    string? Text = null,
    GeminiFunctionCall? FunctionCall = null,
    GeminiFunctionResponse? FunctionResponse = null,
    string? ThoughtSignature = null);

internal record GeminiFunctionCall(string Name, JsonElement Args, string? Id);
internal record GeminiFunctionResponse(string Name, object Response, string? Id);

internal record GeminiTool(List<GeminiFunctionDeclaration> FunctionDeclarations);
internal record GeminiFunctionDeclaration(string Name, string Description, object Parameters);

internal record GeminiResponse(List<GeminiCandidate>? Candidates, GeminiUsage? UsageMetadata);
internal record GeminiCandidate(GeminiContent? Content, string? FinishReason);
internal record GeminiUsage(int PromptTokenCount, int CandidatesTokenCount);