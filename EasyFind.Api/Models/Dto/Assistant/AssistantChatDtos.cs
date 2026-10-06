namespace EasyFind.Api.Models.Dto.Assistant;

public record AssistantMessageDto(string Role, string Text);   // Role: "user" or "assistant"

public record AssistantChatRequest(List<AssistantMessageDto> Messages);

public record AssistantChatResponse(string Reply);