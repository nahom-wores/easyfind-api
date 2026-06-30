using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Services;

public class ChapaClient : IChapaClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ChapaClient> _logger;
    private readonly string _baseUrl;

    public ChapaClient(HttpClient http, IConfiguration config, ILogger<ChapaClient> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = config["Chapa:BaseUrl"] ?? "https://api.chapa.co/v1";

        var secretKey = config["Chapa:SecretKey"]
                        ?? throw new InvalidOperationException("Chapa:SecretKey not configured.");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", secretKey);
    }
    public async Task<string> InitializePaymentAsync(ChapaInitializeRequest request, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync($"{_baseUrl}/transaction/initialize", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Chapa initialize failed. Status {Status}, Body {Body}",
                    response.StatusCode, body);
                return null;
            }

            var result = JsonSerializer.Deserialize<ChapaInitializeResponse>(body);
            if (result?.Status != "success" || result.Data == null)
            {
                _logger.LogError("Chapa initialize returned non-success: {Body}", body);
                return null;
            }

            return result.Data.CheckoutUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception calling Chapa initialize for {TxRef}", request.TxRef);
            return null;
        }
    }

    public async Task<ChapaVerifyData> VerifyPaymentAsync(string txRef, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/transaction/verify/{txRef}", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Chapa verify failed for {TxRef}. Status {Status}, Body {Body}",
                    txRef, response.StatusCode, body);
                return null;
            }

            var result = JsonSerializer.Deserialize<ChapaVerifyResponse>(body);
            if (result?.Status != "success" || result.Data == null)
            {
                _logger.LogWarning("Chapa verify non-success for {TxRef}: {Body}", txRef, body);
                return null;
            }

            return result.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception calling Chapa verify for {TxRef}", txRef);
            return null;
        }
    }
}