using System.Text.Json.Serialization;

namespace EasyFind.Api.Models.Subscriptions;

// Request we send to Chapa's initialize endpoint
public class ChapaInitializeRequest
{
    [JsonPropertyName("amount")] public string Amount { get; set; } = "";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "ETB";
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("tx_ref")] public string TxRef { get; set; } = "";
    [JsonPropertyName("callback_url")] public string CallbackUrl { get; set; } = "";
    [JsonPropertyName("return_url")] public string ReturnUrl { get; set; } = "";
}

// Chapa's response to initialize
public class ChapaInitializeResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public ChapaInitData? Data { get; set; }
}

public class ChapaInitData
{
    [JsonPropertyName("checkout_url")] public string CheckoutUrl { get; set; } = "";
}

// Chapa's response to verify
public class ChapaVerifyResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("data")] public ChapaVerifyData? Data { get; set; }
}

public class ChapaVerifyData
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = "";
    [JsonPropertyName("tx_ref")] public string TxRef { get; set; } = "";
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}