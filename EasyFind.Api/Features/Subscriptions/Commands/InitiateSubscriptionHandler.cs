using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Subscriptions;
using EasyFind.Api.Models.Options;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyFind.Api.Features.Subscriptions.Commands;

public sealed record InitiateSubscriptionCommand(string UserId, SubscriptionTier Tier);

// Starts a Chapa checkout. The pending Payment row is written BEFORE we
// call Chapa, so a webhook that arrives first still finds something to settle.
public class InitiateSubscriptionHandler(
    ApplicationDbContext db,
    IChapaClient chapa,
    UserManager<ApplicationUser> userManager,
    IOptions<SubscriptionOptions> subOptions)
{
    private readonly SubscriptionOptions _opts = subOptions.Value;

    public async Task<Result<CheckoutResponseDto>> HandleAsync(InitiateSubscriptionCommand command, CancellationToken ct = default)
    {
        var (userId, tier) = command;
        if (tier == SubscriptionTier.Free)
            return Result<CheckoutResponseDto>.Validation("Cannot purchase the Free tier.");

        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return Result<CheckoutResponseDto>.NotFound("User not found.");

        var amount = tier switch
        {
            SubscriptionTier.Pro => _opts.ProPriceEtb,
            _ => 0
        };
        if (amount <= 0)
            return Result<CheckoutResponseDto>.Failure("Invalid plan pricing.", ErrorType.Failure);

        // Generate our unique reference
        var txRef = $"easyfind-{Guid.NewGuid():N}";

        // Record the pending payment BEFORE calling Chapa
        var payment = new Payment
        {
            UserId = userId,
            TxRef = txRef,
            Tier = tier,
            AmountEtb = amount,
            Status = PaymentStatus.Pending,
            Provider = PaymentProvider.Chapa,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        // Call Chapa initialize
        var initRequest = new ChapaInitializeRequest
        {
            Amount = amount.ToString(),
            Currency = "ETB",
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            TxRef = txRef,
            CallbackUrl = _opts.CallbackUrl ?? "",
            ReturnUrl = _opts.ReturnUrl ?? "",
        };

        var checkoutUrl = await chapa.InitializePaymentAsync(initRequest, ct);

        if (string.IsNullOrEmpty(checkoutUrl))
        {
            // Chapa failed — mark the payment failed so it's not left dangling
            payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return Result<CheckoutResponseDto>.Failure(
                "Could not start payment. Please try again.", ErrorType.Failure);
        }

        return Result<CheckoutResponseDto>.Success(new CheckoutResponseDto
        {
            CheckoutUrl = checkoutUrl,
            TxRef = txRef
        });
    }
}
