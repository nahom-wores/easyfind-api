using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models.Options;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    [Required] public string Model { get; set; } = "";
    [Required] public string ApiKey { get; set; } = "";
}