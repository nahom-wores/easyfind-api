using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Services.Gemini;

public class GeminiOptions
{
    public const string Section = "Gemini";

    [Required] public string Model { get; init; } = "";
    [Required] public string ApiKey { get; init; } = "";
}