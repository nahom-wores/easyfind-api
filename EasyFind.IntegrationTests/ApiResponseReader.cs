using System.Net.Http.Json;
using System.Text.Json;

namespace EasyFind.IntegrationTests;

// Every endpoint wraps its payload in ApiResponse { isSuccess, errors, result }.
// These pull the typed payload out of `result`.
public static class ApiResponseReader
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);   // camelCase, as ASP.NET writes it

    public static async Task<T> ReadResultAsync<T>(this HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        if (!envelope.TryGetProperty("result", out var result) ||
            result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new InvalidOperationException(
                "Response carried no result payload: " + envelope);

        return result.Deserialize<T>(Json)!;
    }

    public static async Task<string> ReadErrorsAsync(this HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return envelope.TryGetProperty("errors", out var errors)
            ? string.Join("; ", errors.EnumerateArray().Select(e => e.GetString()))
            : "";
    }
}

// Mirrors PagedResult<T> for deserialization.
public class PagedResponse<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Mirrors ListingFeedItemDto — only the fields the tests assert on.
public class FeedItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Organization { get; set; }
    public string? ApplyUrl { get; set; }
    public bool IsLocked { get; set; }
    public bool IsBookmarked { get; set; }
}
