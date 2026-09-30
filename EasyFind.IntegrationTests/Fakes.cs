using System.Collections.Concurrent;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;
using EasyFind.Contracts;

namespace EasyFind.IntegrationTests;

// Stand-ins for the adapters that would otherwise make real network calls.
// Registered as singletons so a test can configure them up front and inspect
// what the application did with them afterwards.

public class FakeSmsService : ISmsService
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _sent = new();

    // Set false to simulate the SMS gateway refusing the message.
    public bool ShouldSucceed { get; set; } = true;

    public IReadOnlyCollection<(string Phone, string Message)> Sent => _sent.ToArray();

    // The OTP most recently texted to this number — how a test "reads the SMS".
    public string? LastOtpFor(string phone) =>
        _sent.Where(s => s.Phone == phone).Select(s => s.Message).LastOrDefault();

    public Task<bool> SendOTPAsync(string toPhoneNumber, string otpCode)
    {
        if (ShouldSucceed) _sent.Enqueue((toPhoneNumber, otpCode));
        return Task.FromResult(ShouldSucceed);
    }

    public Task<bool> SendNotificationAsync(string toPhoneNumber, string message)
    {
        if (ShouldSucceed) _sent.Enqueue((toPhoneNumber, message));
        return Task.FromResult(ShouldSucceed);
    }
}

public class FakeChapaClient : IChapaClient
{
    public string? CheckoutUrl { get; set; } = "https://checkout.chapa.invalid/pay/test";

    // What verification reports back. Null models "Chapa does not know this ref".
    public ChapaVerifyData? VerifyResult { get; set; }

    public int VerifyCallCount { get; private set; }

    public Task<string?> InitializePaymentAsync(ChapaInitializeRequest request, CancellationToken ct = default)
        => Task.FromResult(CheckoutUrl);

    public Task<ChapaVerifyData?> VerifyPaymentAsync(string txRef, CancellationToken ct = default)
    {
        VerifyCallCount++;
        return Task.FromResult(VerifyResult);
    }

    // Convenience: a successful verification for the given reference and amount.
    public void WillVerifySuccessfully(string txRef, int amountEtb, string reference = "chapa-ref-1")
        => VerifyResult = new ChapaVerifyData
        {
            Status = "success",
            Amount = amountEtb,
            Currency = "ETB",
            TxRef = txRef,
            Reference = reference
        };
}

public class FakeNotificationPublisher : INotificationPublisher
{
    private readonly ConcurrentQueue<PaymentSuccessPayload> _paymentSuccess = new();

    public IReadOnlyCollection<PaymentSuccessPayload> PaymentSuccessPublished => _paymentSuccess.ToArray();

    // Set true to simulate SQS being unreachable.
    public bool ShouldThrow { get; set; }

    public Task PublishPaymentSuccessAsync(PaymentSuccessPayload payload, CancellationToken ct = default)
    {
        if (ShouldThrow) throw new HttpRequestException("SQS unreachable (simulated)");
        _paymentSuccess.Enqueue(payload);
        return Task.CompletedTask;
    }
}
