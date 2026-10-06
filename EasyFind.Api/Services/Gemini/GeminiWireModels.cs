namespace EasyFind.Api.Services.Gemini;

internal record GeminiRequest(GeminiContent? SystemInstruction, List<GeminiContent> Contents);
internal record GeminiContent(string? Role, List<GeminiPart> Parts);
internal record GeminiPart(string? Text);

internal record GeminiResponse(List<GeminiCandidate>? Candidates, GeminiUsage? UsageMetadata);
internal record GeminiCandidate(GeminiContent? Content, string? FinishReason);
internal record GeminiUsage(int PromptTokenCount, int CandidatesTokenCount);