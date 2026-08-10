using EasyFind.Api;
using EasyFind.Api.Models.Enum;
using FluentAssertions;

namespace EasyFind.UnitTests;

public class ListingScorerTests
{
    [Fact]
    public void Job_MatchingCountryAndCategory_ScoresBoth()
    {
         var score = ListingScorer.Score(
            type: ListingType.Job,
            countryCode: "DE",
            jobCategory: 0,          // IT
            scholarshipField: null,
            degreeLevel: null,
            isFeatured: false,
            targetCountries: new() { "DE" },
            jobCats: new() { 0 },     // wants IT
            schFields: new(),
            targetDegree: null);
        score.Should().Be(80);
    }
    [Fact]
    public void Job_NoMatches_ScoresZero()
    {
        var score = ListingScorer.Score(
            type: ListingType.Job,
            countryCode: "CA",       // user doesn't want Canada
            jobCategory: 5,          // user doesn't want this category
            scholarshipField: null,
            degreeLevel: null,
            isFeatured: false,
            targetCountries: new() { "DE" },
            jobCats: new() { 0 },
            schFields: new(),
            targetDegree: null);

        score.Should().Be(0);
    }
    [Fact]
    public void FeaturedListing_AddsFeaturedWeight()
    {
        var score = ListingScorer.Score(
            type: ListingType.Job,
            countryCode: "DE",
            jobCategory: 0,
            scholarshipField: null,
            degreeLevel: null,
            isFeatured: true,        // ← featured
            targetCountries: new() { "DE" },
            jobCats: new() { 0 },
            schFields: new(),
            targetDegree: null);

        // country 50 + category 30 + featured 10 = 90
        score.Should().Be(90);
    }
    [Theory]   // ← runs the same test with different inputs
    [InlineData("DE", 50)]   // wants DE, listing is DE → 50
    [InlineData("CA", 0)]    // wants DE, listing is CA → 0
    public void CountryMatch_ScoresCorrectly(string listingCountry, int expected)
    {
        var score = ListingScorer.Score(
            type: ListingType.Job,
            countryCode: listingCountry,
            jobCategory: null,
            scholarshipField: null,
            degreeLevel: null,
            isFeatured: false,
            targetCountries: new() { "DE" },
            jobCats: new(),
            schFields: new(),
            targetDegree: null);

        score.Should().Be(expected);
    }
}