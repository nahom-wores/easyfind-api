
using EasyFind.Contracts;

namespace EasyFind.Api.Services.IServices;

public interface INotificationPublisher
{
    Task PublishPaymentSuccessAsync(PaymentSuccessPayload payload, CancellationToken ct = default);
}