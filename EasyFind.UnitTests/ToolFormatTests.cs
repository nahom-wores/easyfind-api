using EasyFind.Api.Models.Enum;
using EasyFind.Api.Services.Assistant.Tools;
using FluentAssertions;

namespace EasyFind.UnitTests;

public class ToolFormatTests
{
    [Fact]
    public void Shorten_LeavesShortTextAlone()
        => ToolFormat.Shorten("  Night shifts.  ", 100).Should().Be("Night shifts.");

    [Fact]
    public void Shorten_CutsAtAWordBoundary_AndMarksIt()
    {
        var result = ToolFormat.Shorten("alpha beta gamma delta", 13);

        result.Should().Be("alpha beta" + ToolFormat.TruncatedMarker);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Shorten_EmptyInput_GivesEmpty(string? text)
        => ToolFormat.Shorten(text, 10).Should().BeEmpty();

    [Fact]
    public void EnumName_KnownValue_GivesTheName()
        => ToolFormat.EnumName<FundingType>((int)FundingType.FullyFunded).Should().Be("FullyFunded");

    [Theory]
    [InlineData(null)]
    [InlineData(12345)]
    public void EnumName_MissingOrUnknown_GivesNull(int? value)
        => ToolFormat.EnumName<FundingType>(value).Should().BeNull();
}
