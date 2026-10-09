using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.Assistant.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyFind.IntegrationTests;

// get_listing_details and recommend_listings, end to end: a signed-in request
// to POST /assistant/chat, the fake model asks for the tool, and the test reads
// what the tool handed back to the model.
//
// Each test uses its own country codes so it only ranks and finds its own rows.
public class AssistantToolTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private FakeChatModel Model => factory.Services.GetRequiredService<FakeChatModel>();

    private async Task<JsonElement> RunToolAsync(HttpClient client, string tool, object arguments)
    {
        Model.ScriptToolCall(tool, arguments);

        var response = await client.PostAsJsonAsync("/api/v1/assistant/chat",
            new { messages = new[] { new { role = "user", text = "hi" } } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Model.LastToolResult();
    }

    private static Listing Job(string country, string title = "Job", string description = "desc", bool isActive = true) => new()
    {
        Type = ListingType.Job,
        Title = title,
        Organization = "Acme Hospital",
        CountryCode = country,
        Description = description,
        ApplyUrl = "https://apply.example.com/job",
        IsActive = isActive,
        JobCategory = JobCategory.Nursing,
        EmploymentType = EmploymentType.FullTime,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Listing Scholarship(string country, string description = "desc") => new()
    {
        Type = ListingType.Scholarship,
        Title = "Scholarship",
        Organization = "Acme University",
        CountryCode = country,
        Description = description,
        ApplyUrl = "https://apply.example.com/sch",
        IsActive = true,
        ScholarshipField = ScholarshipField.ComputerScience,
        DegreeLevel = DegreeLevel.Masters,
        FundingType = FundingType.FullyFunded,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private Task SeedAsync(params Listing[] listings) => factory.SeedAsync(db => db.Listings.AddRange(listings));

    private Task GiveProfileAsync(string userId, params string[] targetCountries) =>
        GiveProfileAsync(userId, SeekingType.Both, targetCountries);

    private Task GiveProfileAsync(string userId, SeekingType seeking, params string[] targetCountries) =>
        factory.SeedAsync(db => db.UserProfiles.Add(new UserProfile
        {
            UserId = userId,
            SeekingType = seeking,
            TargetCountries = targetCountries.ToList(),
        }));

    private static string LongText(int words) => string.Join(' ', Enumerable.Repeat("word", words));

    // ── recommend_listings ──────────────────────────────────────────────────

    // The guarantee the feature rests on: the user is the signed-in caller, and
    // a userId slipped into the arguments changes nothing.
    [Fact]
    public async Task Recommend_UsesTheSignedInUser_EvenWhenArgumentsNameSomeoneElse()
    {
        var (client, me) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        var (_, other) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        await GiveProfileAsync(me.Id, "RA");
        await GiveProfileAsync(other.Id, "RB");
        await SeedAsync(Job("RA", "mine"), Job("RB", "theirs"));

        var result = await RunToolAsync(client, "recommend_listings", new { userId = other.Id });

        result.GetProperty("profile").GetProperty("targetCountries").EnumerateArray()
            .Select(c => c.GetString()).Should().Equal("RA");
        result.GetProperty("listings")[0].GetProperty("countryCode").GetString()
            .Should().Be("RA", "the caller's target country ranks first, not the other user's");
    }

    [Fact]
    public async Task Recommend_FreeUser_IsCappedAndGated_LikeTheFeed()
    {
        var (client, me) = await factory.SignedInUserAsync(SubscriptionTier.Free);
        await GiveProfileAsync(me.Id, "RC");
        await SeedAsync(Enumerable.Range(0, 7).Select(i => Job("RC", $"job-{i}")).ToArray());

        var result = await RunToolAsync(client, "recommend_listings", new { countryCode = "RC" });

        var listings = result.GetProperty("listings").EnumerateArray().ToList();
        listings.Should().HaveCountLessThanOrEqualTo(5);
        listings.Should().OnlyContain(l => l.GetProperty("organization").ValueKind == JsonValueKind.Null);
        listings.Should().OnlyContain(l => l.GetProperty("locked").GetBoolean());
    }

    [Fact]
    public async Task Recommend_TypeArgument_ReturnsOnlyThatType()
    {
        var (client, me) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        await GiveProfileAsync(me.Id, "RD");
        await SeedAsync(Job("RD"), Scholarship("RD"));

        var result = await RunToolAsync(client, "recommend_listings", new { type = "scholarship", countryCode = "RD" });

        result.GetProperty("listings").EnumerateArray()
            .Select(l => l.GetProperty("type").GetString()).Should().Equal("Scholarship");
    }

    [Fact]
    public async Task Recommend_GivesReadableAttributes_AndAShortSnippet()
    {
        var (client, me) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        await GiveProfileAsync(me.Id, "RE");
        await SeedAsync(Scholarship("RE", description: LongText(300)));

        var result = await RunToolAsync(client, "recommend_listings", new { countryCode = "RE" });

        var listing = result.GetProperty("listings")[0];
        listing.GetProperty("category").GetString().Should().Be("ComputerScience");
        listing.GetProperty("degreeLevel").GetString().Should().Be("Masters");
        listing.GetProperty("fundingType").GetString().Should().Be("FullyFunded");

        var snippet = listing.GetProperty("snippet").GetString()!;
        snippet.Should().EndWith(ToolFormat.TruncatedMarker);
        snippet.Length.Should().BeLessThanOrEqualTo(ToolFormat.SnippetChars + ToolFormat.TruncatedMarker.Length);
    }

    // A jobs-only profile asking for scholarships always gets nothing, even when
    // scholarships exist. The note is what stops the bot saying "there are none".
    [Fact]
    public async Task Recommend_TypeExcludedByProfile_ExplainsWhyItIsEmpty()
    {
        var (client, me) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        await GiveProfileAsync(me.Id, SeekingType.Job, "RG");
        await SeedAsync(Scholarship("RG"));

        var result = await RunToolAsync(client, "recommend_listings", new { type = "scholarship", countryCode = "RG" });

        result.GetProperty("count").GetInt32().Should().Be(0);
        var note = result.GetProperty("note").GetString();
        note.Should().Contain("set to jobs only").And.Contain("profile settings");
    }

    [Theory]
    [InlineData(SeekingType.Both, "scholarship")]      // profile allows both
    [InlineData(SeekingType.Scholarship, "scholarship")] // asked for what the profile wants
    [InlineData(SeekingType.Job, null)]                // no type asked for
    public async Task Recommend_NoNote_WhenTheProfileDoesNotExcludeTheType(SeekingType seeking, string? type)
    {
        var (client, me) = await factory.SignedInUserAsync(SubscriptionTier.Pro);
        await GiveProfileAsync(me.Id, seeking, "RH");

        var result = await RunToolAsync(client, "recommend_listings", new { type, countryCode = "RH" });

        result.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Recommend_WithoutAProfile_SaysResultsAreNotPersonalized()
    {
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var result = await RunToolAsync(client, "recommend_listings", new { countryCode = "RF" });

        result.GetProperty("profile").GetProperty("missing").GetBoolean().Should().BeTrue();
    }

    // ── get_listing_details ─────────────────────────────────────────────────

    [Theory]
    [InlineData(SubscriptionTier.Free, false)]
    [InlineData(SubscriptionTier.Pro, true)]
    public async Task Details_OrganizationAndApplyUrl_FollowThePaywall(SubscriptionTier tier, bool visible)
    {
        var listing = Job("DA");
        await SeedAsync(listing);
        var (client, _) = await factory.SignedInUserAsync(tier);

        var result = await RunToolAsync(client, "get_listing_details", new { listingId = listing.Id });

        result.GetProperty("found").GetBoolean().Should().BeTrue();
        result.GetProperty("locked").GetBoolean().Should().Be(!visible);
        if (visible)
        {
            result.GetProperty("organization").GetString().Should().Be("Acme Hospital");
            result.GetProperty("applyUrl").GetString().Should().Be("https://apply.example.com/job");
        }
        else
        {
            result.GetProperty("organization").ValueKind.Should().Be(JsonValueKind.Null);
            result.GetProperty("applyUrl").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Details_GivesReadableAttributes()
    {
        var listing = Job("DB");
        await SeedAsync(listing);
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var result = await RunToolAsync(client, "get_listing_details", new { listingId = listing.Id });

        result.GetProperty("jobCategory").GetString().Should().Be("Nursing");
        result.GetProperty("employmentType").GetString().Should().Be("FullTime");
    }

    [Fact]
    public async Task Details_LongDescription_IsShortened_ShortOneIsUntouched()
    {
        var longOne = Job("DC", description: LongText(1000));
        var shortOne = Job("DC", description: "Night shifts, German B1 required.");
        await SeedAsync(longOne, shortOne);
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var cut = (await RunToolAsync(client, "get_listing_details", new { listingId = longOne.Id }))
            .GetProperty("description").GetString()!;
        cut.Should().EndWith(ToolFormat.TruncatedMarker);
        cut.Length.Should().BeLessThanOrEqualTo(ToolFormat.DetailDescriptionChars + ToolFormat.TruncatedMarker.Length);

        (await RunToolAsync(client, "get_listing_details", new { listingId = shortOne.Id }))
            .GetProperty("description").GetString().Should().Be("Night shifts, German B1 required.");
    }

    [Fact]
    public async Task Details_ClosedOrUnknownListing_IsNotFound()
    {
        var closed = Job("DD", isActive: false);
        await SeedAsync(closed);
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        (await RunToolAsync(client, "get_listing_details", new { listingId = closed.Id }))
            .GetProperty("found").GetBoolean().Should().BeFalse();
        (await RunToolAsync(client, "get_listing_details", new { listingId = Guid.NewGuid() }))
            .GetProperty("found").GetBoolean().Should().BeFalse();
    }

    // The model can send anything; a bad id must come back as an error it can
    // read, not a failed request.
    [Fact]
    public async Task Details_MalformedId_ReturnsAnErrorToTheModel()
    {
        var (client, _) = await factory.SignedInUserAsync(SubscriptionTier.Pro);

        var result = await RunToolAsync(client, "get_listing_details", new { listingId = "the first one" });

        result.TryGetProperty("error", out _).Should().BeTrue();
    }
}
