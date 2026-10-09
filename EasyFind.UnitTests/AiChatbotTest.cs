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
    [Fact]
    public void SystemPrompt_HasNoUnfilledPlaceholders()
    {
        var prompt = AssistantPrompts.BuildSystem(Sub, "Pro");

        prompt.Should().NotContain("{");
        prompt.Should().NotContain("}");
    }
}