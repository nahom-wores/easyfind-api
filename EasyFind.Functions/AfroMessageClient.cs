using System.Net.Http.Headers;
using System.Text.Json;

namespace EasyFind.Functions;

public class AfroMessageClient(HttpClient http, string token,
    string identifierId, string senderName)
{
    private const string BaseUrl = "https://api.afromessage.com/api/send";
    
    // Throws on any failure, so SQS retries the message
    public async Task SendAsync(string phone, string message, CancellationToken ct = default)
    {
        var to = Normalize(phone);
        var url = $"{BaseUrl}?from={Uri.EscapeDataString(identifierId)}" +
                  $"&sender={Uri.EscapeDataString(senderName)}" +
                  $"&to={Uri.EscapeDataString(to)}" +
                  $"&message={Uri.EscapeDataString(message)}&callback=";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"AfroMessage HTTP {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var ack = doc.RootElement.TryGetProperty("acknowledge", out var a) ? a.GetString() : null;
        if (!string.Equals(ack, "success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"AfroMessage rejected: {body}");
    }
    private static string Normalize(string phone)
    {
        phone = phone.Trim();
        if (phone.StartsWith('+')) return phone[1..];
        if (phone.StartsWith('0') && phone.Length == 10) return "251" + phone[1..];
        return phone;
    }
}