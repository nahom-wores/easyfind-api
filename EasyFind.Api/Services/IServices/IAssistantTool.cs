using System.Text.Json;

namespace EasyFind.Api.Services.IServices;

public interface IAssistantTool
{
    /// What the model sees.
    ToolDefinition Definition { get; }

    /// What runs when the model asks for this tool. Returns any object; it's serialized to JSON for the model.
    Task<object> ExecuteAsync(JsonElement args, CancellationToken ct);
}