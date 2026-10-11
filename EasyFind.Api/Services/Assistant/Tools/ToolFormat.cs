using System.Text.Json;

namespace EasyFind.Api.Services.Assistant.Tools;

// Shared by the assistant tools: reading the model's arguments, and shaping
// what goes back to it.
public static class ToolFormat
{
    // Listing descriptions are the bulk of what a tool sends the model, and
    // every token is paid for on every round of the conversation.
    public const int DetailDescriptionChars = 1500;   // get_listing_details
    public const int SnippetChars = 200;              // recommend_listings, per result

    public const string TruncatedMarker = " …(truncated)";

    // The model's arguments are UNTRUSTED input — read them like any user input.
    public static string? GetString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // DTOs carry enums as ints; the model needs the name to say anything useful.
    // An unknown value comes back null rather than a bare number.
    public static string? EnumName<TEnum>(int? value) where TEnum : struct, Enum =>
        value is { } v && Enum.IsDefined(typeof(TEnum), v)
            ? Enum.GetName(typeof(TEnum), v)
            : null;

    // Cuts at a word boundary and says so, so the model knows text is missing
    // rather than treating the cut-off as the end of the listing.
    public static string Shorten(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars) return trimmed;

        var cut = trimmed[..maxChars];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxChars / 2) cut = cut[..lastSpace];

        return cut.TrimEnd() + TruncatedMarker;
    }
}
