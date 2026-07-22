using System.Net.Http.Headers;
using System.Text.Json;
using EasyFind.Api.Services.IServices;


namespace EasyFind.Api.Services;

public class AfroSmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AfroSmsService> _logger;
    private readonly string _apiToken;
    private readonly string _identifierId;
    private readonly string _senderName;

    private const string BASE_URL = "https://api.afromessage.com/api/send";

    public AfroSmsService(HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AfroSmsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _apiToken = configuration["AfroMessage:ApiToken"]
                    ?? throw new InvalidOperationException("AfroMessage:ApiToken is not configured.");
        _identifierId = configuration["AfroMessage:IdentifierId"] ?? string.Empty;
        _senderName = configuration["AfroMessage:SenderName"] ?? string.Empty;
    }

    public async Task<bool> SendOTPAsync(string toPhoneNumber, string otpCode)
    {
        var message = $"Your Yisru verification code is: {otpCode}. Valid for 5 minutes.";
        return await SendAsync(toPhoneNumber, message);
    }

    public async Task<bool> SendNotificationAsync(string toPhoneNumber, string message)
    {
        return await SendAsync(toPhoneNumber, message);
    }

    private async Task<bool> SendAsync(string toPhoneNumber, string message)
    {
        try
        {
            var normalizedPhone = NormalizePhoneNumber(toPhoneNumber);

            // AfroMessage uses GET with query parameters
            var url = new UriBuilder(BASE_URL);
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["from"] = _identifierId;
            query["sender"] = _senderName;
            query["to"] = normalizedPhone;
            query["message"] = message;
            query["callback"] = "";
            url.Query = query.ToString();

            // Auth goes in the header
            var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("AfroMessage HTTP error. Status: {Status}, Body: {Body}",
                    response.StatusCode, responseBody);
                return false;
            }

            var result = JsonSerializer.Deserialize<AfroMessageResponse>(responseBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Acknowledge?.ToLower() != "success")
            {
                _logger.LogWarning("AfroMessage returned error. Body: {Body}", responseBody);
                return false;
            }

            _logger.LogInformation("SMS sent successfully to {Phone}", normalizedPhone);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending SMS to {Phone}", toPhoneNumber);
            return false;
        }
    }

    private static string NormalizePhoneNumber(string phone)
    {
        phone = phone.Trim();

        if (phone.StartsWith("+"))
            return phone[1..];

        if (phone.StartsWith("0") && phone.Length == 10)
            return "251" + phone[1..];

        return phone;
    }

    internal class AfroMessageResponse
    {
        public string? Acknowledge { get; set; }
        public AfroMessageResponseData? Response { get; set; }
    }

    internal class AfroMessageResponseData
    {
        public string? Id { get; set; }
        public string? To { get; set; }
        public string? Status { get; set; }
    }
}