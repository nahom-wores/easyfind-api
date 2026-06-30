using EasyFind.Api.Models.Subscriptions;

namespace EasyFind.Api.Services.IServices;

public interface IChapaClient
{
    Task<string?> InitializePaymentAsync(ChapaInitializeRequest request, CancellationToken ct = default);
    Task<ChapaVerifyData?> VerifyPaymentAsync(string txRef, CancellationToken ct = default);
}