using EasyFind.Api.Models.Options;
using EasyFind.Api.Prompts;
using FluentAssertions;

namespace EasyFind.UnitTests;

public class AiChatbotTest
{
    private static readonly SubscriptionOptions Sub =
        new() { ProPriceEtb = 799, DurationDays = 14, FreeFeedCap = 3 };

    [Fact]
    public void SystemPrompt_UsesConfiguredPrice()
    {
        var prompt = AssistantPrompts.BuildSystem(Sub, "Pro");

        prompt.Should().Contain("799 ETB for 14 days");
        prompt.Should().Contain("users see 3 listings");
    }
    [Fact]
    public void SystemPrompt_IncludesUserPlan()
    {
        var prompt = AssistantPrompts.BuildSystem(Sub, "Pro");

        prompt.Should().Contain("Plan: Pro");
    }
    // A tool the prompt never mentions is one the model rarely uses well.
    [Theory]
    [InlineData("search_listings")]
    [InlineData("get_listing_details")]
    [InlineData("recommend_listings")]
    public void SystemPrompt_ExplainsEveryTool(string tool)
        => AssistantPrompts.BuildSystem(Sub, "Pro").Should().Contain(tool);

    [Fact]
    public void SystemPrompt_HasNoUnfilledPlaceholders()
    {
        var prompt = AssistantPrompts.BuildSystem(Sub, "Pro");

        prompt.Should().NotContain("{");
        prompt.Should().NotContain("}");
    }
}