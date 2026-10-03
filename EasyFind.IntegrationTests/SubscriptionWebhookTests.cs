using EasyFind.Api.Features.Subscriptions.Commands;
using EasyFind.Api.Models.Subscriptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// Chapa delivers the same payment twice by design — a server webhook and a
// browser callback — and retries on failure. So this handler runs more than once
// per payment, and everything here is about it staying idempotent.
//
// A regression means either double-extending a subscription the user paid once
// for, or activating one that was never paid.
public class SubscriptionWebhookTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private const int ProPrice = 499;
    private const int DurationDays = 30;

    private async Task<(string userId, string txRef)> SeedPendingPaymentAsync(int amount = ProPrice)
    {
        var (_, user) = await factory.SignedInUserAsync();
        var txRef = "easyfind-" + Guid.NewGuid().ToString("N");

        await factory.SeedAsync(db => db.Payments.Add(new Payment
        {
            UserId = user.Id,
            TxRef = txRef,
            Tier = SubscriptionTier.Pro,
            AmountEtb = amount,
            Status = PaymentStatus.Pending,
            Provider = PaymentProvider.Chapa,
        }));

        return (user.Id, txRef);
    }

    private Task<T> RunHandlerAsync<T>(Func<ProcessChapaPaymentHandler, Task<T>> act)
        => factory.WithHandlerAsync(sp =>
            act(sp.GetRequiredService<ProcessChapaPaymentHandler>()));

    [Fact]
    public async Task DoubleDelivery_ActivatesExactlyOneSubscription()
    {
        var (userId, txRef) = await SeedPendingPaymentAsync();
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();
        chapa.WillVerifySuccessfully(txRef, ProPrice);

        // The webhook and the callback, both for the same payment.
        var first = await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));
        var second = await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue("a repeat delivery must be a no-op, not an error");

        var subs = await factory.WithDbAsync(db => db.Subscriptions
            .Where(s => s.UserId == userId).ToListAsync());

        subs.Should().HaveCount(1, "the second delivery must not create another subscription");
        subs[0].Tier.Should().Be(SubscriptionTier.Pro);
        subs[0].Status.Should().Be(SubscriptionStatus.Active);

        // The decisive assertion: paid once, so ~30 days — not 60.
        subs[0].ExpiresAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddDays(DurationDays), TimeSpan.FromMinutes(5),
            "a duplicate delivery must not extend the subscription a second time");

        var payment = await factory.WithDbAsync(db =>
            db.Payments.SingleAsync(p => p.TxRef == txRef));
        payment.Status.Should().Be(PaymentStatus.Success);
        payment.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DoubleDelivery_PublishesThePaymentSmsExactlyOnce()
    {
        // The SMS rides on the same guard as the activation: only the delivery
        // that flips the payment to Success publishes. A duplicate callback that
        // published again would text the user twice for one payment.
        var (userId, txRef) = await SeedPendingPaymentAsync();
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();
        chapa.WillVerifySuccessfully(txRef, ProPrice);
        var publisher = factory.Services.GetRequiredService<FakeNotificationPublisher>();

        await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));

        var published = publisher.PaymentSuccessPublished.Where(p => p.TxRef == txRef).ToList();
        published.Should().ContainSingle("the first successful delivery publishes once");
        published[0].UserId.Should().Be(userId);
        published[0].PhoneNumber.Should().NotBeNullOrWhiteSpace();
        published[0].AmountEtb.Should().Be(ProPrice);
        published[0].Tier.Should().Be("Pro");

        await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));

        publisher.PaymentSuccessPublished.Count(p => p.TxRef == txRef).Should().Be(1,
            "a duplicate callback must publish nothing");
    }

    [Fact]
    public async Task PublishFailure_DoesNotFailOrUndoTheActivation()
    {
        // The subscription is committed before publishing. If SQS is down, the
        // payment must still succeed (a 500 would only make Chapa retry into the
        // idempotency guard) and the user must still be Pro.
        var (userId, txRef) = await SeedPendingPaymentAsync();
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();
        chapa.WillVerifySuccessfully(txRef, ProPrice);
        var publisher = factory.Services.GetRequiredService<FakeNotificationPublisher>();

        publisher.ShouldThrow = true;
        try
        {
            var result = await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));
            result.IsSuccess.Should().BeTrue("a failed notification must not fail the payment");
        }
        finally
        {
            publisher.ShouldThrow = false;
        }

        var payment = await factory.WithDbAsync(db => db.Payments.SingleAsync(p => p.TxRef == txRef));
        payment.Status.Should().Be(PaymentStatus.Success);
        var user = await factory.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == userId));
        user.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
    }

    [Fact]
    public async Task SuccessfulPayment_MirrorsTierOntoTheUser()
    {
        // The feed reads ApplicationUser.SubscriptionTier, not the Subscription
        // row, so if this mirror is not written the customer pays and stays gated.
        var (userId, txRef) = await SeedPendingPaymentAsync();
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();
        chapa.WillVerifySuccessfully(txRef, ProPrice);

        await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));

        var user = await factory.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == userId));
        user.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
    }

    [Fact]
    public async Task AmountMismatch_IsRejected_AndActivatesNothing()
    {
        // Anti-tamper: the callback claims a different amount than we recorded.
        var (userId, txRef) = await SeedPendingPaymentAsync(amount: ProPrice);
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();
        chapa.WillVerifySuccessfully(txRef, amountEtb: 1);   // paid 1 ETB, owes 499

        var result = await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));

        result.IsSuccess.Should().BeTrue("Chapa gets a 200 so it stops retrying");

        var payment = await factory.WithDbAsync(db =>
            db.Payments.SingleAsync(p => p.TxRef == txRef));
        payment.Status.Should().Be(PaymentStatus.Failed);

        var subs = await factory.WithDbAsync(db =>
            db.Subscriptions.CountAsync(s => s.UserId == userId));
        subs.Should().Be(0, "an underpaid transaction must not activate a subscription");
    }

    [Fact]
    public async Task FailedVerification_MarksPaymentFailed_AndActivatesNothing()
    {
        var (userId, txRef) = await SeedPendingPaymentAsync();
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();
        chapa.VerifyResult = null;   // Chapa does not confirm this reference

        var result = await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(txRef)));

        result.IsSuccess.Should().BeTrue();

        var payment = await factory.WithDbAsync(db =>
            db.Payments.SingleAsync(p => p.TxRef == txRef));
        payment.Status.Should().Be(PaymentStatus.Failed);

        (await factory.WithDbAsync(db => db.Subscriptions.CountAsync(s => s.UserId == userId)))
            .Should().Be(0);
    }

    [Fact]
    public async Task UnknownReference_IsAcceptedQuietly()
    {
        // An unknown tx_ref must not make Chapa retry forever, and must not
        // reach the verification call at all.
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();
        var callsBefore = chapa.VerifyCallCount;

        var result = await RunHandlerAsync(h =>
            h.HandleAsync(new ProcessChapaPaymentCommand("no-such-reference")));

        result.IsSuccess.Should().BeTrue();
        chapa.VerifyCallCount.Should().Be(callsBefore,
            "an unknown reference should short-circuit before verifying");
    }

    [Fact]
    public async Task SecondPayment_StacksOntoTheExistingSubscription()
    {
        // Renewing while still active extends from the current expiry rather
        // than restarting from today, so the user loses no paid time.
        var (userId, firstRef) = await SeedPendingPaymentAsync();
        var chapa = factory.Services.GetRequiredService<FakeChapaClient>();

        chapa.WillVerifySuccessfully(firstRef, ProPrice);
        await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(firstRef)));

        var secondRef = "easyfind-" + Guid.NewGuid().ToString("N");
        await factory.SeedAsync(db => db.Payments.Add(new Payment
        {
            UserId = userId,
            TxRef = secondRef,
            Tier = SubscriptionTier.Pro,
            AmountEtb = ProPrice,
            Status = PaymentStatus.Pending,
            Provider = PaymentProvider.Chapa,
        }));

        chapa.WillVerifySuccessfully(secondRef, ProPrice, reference: "chapa-ref-2");
        await RunHandlerAsync(h => h.HandleAsync(new ProcessChapaPaymentCommand(secondRef)));

        var subs = await factory.WithDbAsync(db => db.Subscriptions
            .Where(s => s.UserId == userId).ToListAsync());

        subs.Should().HaveCount(1, "a renewal extends the existing subscription");
        subs[0].ExpiresAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddDays(DurationDays * 2), TimeSpan.FromMinutes(5),
            "two paid periods should stack to roughly 60 days");
    }
}
