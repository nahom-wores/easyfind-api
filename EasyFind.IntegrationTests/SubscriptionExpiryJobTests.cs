using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Services.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// The nightly job that ends lapsed subscriptions.
//
// It maintains an invariant the feed depends on: ApplicationUser.SubscriptionTier
// is a denormalized copy of "this user holds a live subscription", and
// GetFeedHandler reads that column on every request to decide the free result
// cap. Both halves of the job have to agree, and both have to land together —
// they were previously not in a transaction, so a failure mid-run could strand
// a paying user on Free with their subscription still marked Active.
public class SubscriptionExpiryJobTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private Task RunJobAsync() => factory.WithHandlerAsync(async sp =>
    {
        await sp.GetRequiredService<SubscriptionExpiryJob>().RunAsync();
        return true;
    });

    private async Task<string> SeedUserWithSubscriptionsAsync(
        SubscriptionTier tier, params (SubscriptionStatus status, int expiresInDays)[] subscriptions)
    {
        var (_, user) = await factory.SignedInUserAsync(tier);
        var now = DateTimeOffset.UtcNow;

        await factory.SeedAsync(db =>
        {
            foreach (var (status, expiresInDays) in subscriptions)
                db.Subscriptions.Add(new Subscription
                {
                    UserId = user.Id,
                    Tier = SubscriptionTier.Pro,
                    Status = status,
                    StartedAt = now.AddDays(-60),
                    ExpiresAt = now.AddDays(expiresInDays),
                });
        });

        return user.Id;
    }

    private Task<SubscriptionTier> TierOfAsync(string userId)
        => factory.WithDbAsync(db => db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SubscriptionTier)
            .SingleAsync());

    private Task<List<SubscriptionStatus>> StatusesOfAsync(string userId)
        => factory.WithDbAsync(db => db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.Status)
            .ToListAsync());

    [Fact]
    public async Task LapsedSubscription_IsExpired_AndTheUserDropsToFree()
    {
        var userId = await SeedUserWithSubscriptionsAsync(
            SubscriptionTier.Pro, (SubscriptionStatus.Active, -1));   // expired yesterday

        await RunJobAsync();

        (await StatusesOfAsync(userId)).Should().AllBeEquivalentTo(SubscriptionStatus.Expired);
        (await TierOfAsync(userId)).Should().Be(SubscriptionTier.Free,
            "the feed reads this column to apply the free cap");
    }

    [Fact]
    public async Task LiveSubscription_IsUntouched()
    {
        var userId = await SeedUserWithSubscriptionsAsync(
            SubscriptionTier.Pro, (SubscriptionStatus.Active, 10));   // ten days left

        await RunJobAsync();

        (await StatusesOfAsync(userId)).Should().AllBeEquivalentTo(SubscriptionStatus.Active);
        (await TierOfAsync(userId)).Should().Be(SubscriptionTier.Pro,
            "expiring someone who has paid through next week would be the worst kind of bug");
    }

    // THE regression that matters. A user who renews while an old row is lapsing
    // holds two subscriptions: one past its expiry, one live. Downgrading every
    // user who had something expire cuts off a customer who has just paid —
    // including one whose payment lands while this job is running.
    [Fact]
    public async Task RenewedUser_KeepsPro_EvenThoughAnOlderSubscriptionExpires()
    {
        var userId = await SeedUserWithSubscriptionsAsync(
            SubscriptionTier.Pro,
            (SubscriptionStatus.Active, -1),    // the lapsed one
            (SubscriptionStatus.Active, 30));   // the renewal

        await RunJobAsync();

        var statuses = await StatusesOfAsync(userId);
        statuses.Should().HaveCount(2);
        statuses.Should().Contain(SubscriptionStatus.Expired, "the lapsed row must still be closed off");
        statuses.Should().Contain(SubscriptionStatus.Active, "the renewal must survive");

        (await TierOfAsync(userId)).Should().Be(SubscriptionTier.Pro,
            "this user is covered — downgrading them would revoke access they paid for");
    }

    // The tier reconciliation is derived from the subscription rows, not from
    // the set the job just updated, so it also repairs a flag that drifted —
    // a webhook that died between its two writes, or an earlier partial run.
    [Fact]
    public async Task DriftedTier_WithNoSubscriptionAtAll_IsRepaired()
    {
        var (_, user) = await factory.SignedInUserAsync(SubscriptionTier.Pro);   // Pro, no rows

        await RunJobAsync();

        (await TierOfAsync(user.Id)).Should().Be(SubscriptionTier.Free);
    }

    [Fact]
    public async Task RunningTwice_ChangesNothingTheSecondTime()
    {
        var userId = await SeedUserWithSubscriptionsAsync(
            SubscriptionTier.Pro, (SubscriptionStatus.Active, -1));

        await RunJobAsync();
        await RunJobAsync();   // the retry, or simply tomorrow night

        (await StatusesOfAsync(userId)).Should().AllBeEquivalentTo(SubscriptionStatus.Expired);
        (await TierOfAsync(userId)).Should().Be(SubscriptionTier.Free);
    }

    // Cancelled is an admin revocation, a different end state from lapsing. The
    // job must not quietly rewrite it to Expired and lose that distinction.
    [Fact]
    public async Task CancelledSubscription_IsNotRelabelledAsExpired()
    {
        var userId = await SeedUserWithSubscriptionsAsync(
            SubscriptionTier.Free, (SubscriptionStatus.Cancelled, -5));

        await RunJobAsync();

        (await StatusesOfAsync(userId)).Should().AllBeEquivalentTo(SubscriptionStatus.Cancelled);
    }
}
