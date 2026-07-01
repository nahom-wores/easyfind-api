using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Subscriptions;
using EasyFind.Api.Models.Subscriptions;

namespace EasyFind.Api.Services.IServices;

public interface ISubscriptionService
{
    // Start payment
    Task<Result<CheckoutResponseDto>> InitiateAsync(string userId, SubscriptionTier tier, CancellationToken ct = default);
    Task<Result> HandleWebhookAsync(string txRef, CancellationToken ct = default);
    Task<Result<SubscriptionStatusDto>> GetMyStatusAsync(string userId, CancellationToken ct = default);
}