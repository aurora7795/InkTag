using System.Linq;
using InkTag.Core.Scrapers;
using Xunit;

namespace InkTag.Tests;

public class SeriesAliasResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSearchTitles_ReturnsEmpty_ForNullOrWhitespace(string? input)
    {
        var titles = SeriesAliasResolver.ResolveSearchTitles(input);
        Assert.Empty(titles);
    }

    [Fact]
    public void ResolveSearchTitles_ReturnsSingleTitle_WhenNoRulesMatch()
    {
        var titles = SeriesAliasResolver.ResolveSearchTitles("Saga");
        Assert.Single(titles);
        Assert.Equal("Saga", titles[0]);
    }

    [Fact]
    public void ResolveSearchTitles_ExpandsBuffyColonPrefix()
    {
        var titles = SeriesAliasResolver.ResolveSearchTitles("Buffy: The Origin");
        Assert.Equal(3, titles.Count);
        Assert.Equal("Buffy: The Origin", titles[0]);
        Assert.Equal("Buffy the Vampire Slayer: The Origin", titles[1]);
        Assert.Equal("The Origin", titles[2]);
    }

    [Fact]
    public void ResolveSearchTitles_ExpandsBuffySeasonPrefix()
    {
        var titles = SeriesAliasResolver.ResolveSearchTitles("Buffy, Season 8");
        Assert.Equal(2, titles.Count);
        Assert.Equal("Buffy, Season 8", titles[0]);
        Assert.Equal("Buffy the Vampire Slayer Season 8", titles[1]);
    }

    [Fact]
    public void ResolveSearchTitles_ExpandsSubtitle_ForGenericColonSeries()
    {
        var titles = SeriesAliasResolver.ResolveSearchTitles("Batman: The Long Halloween");
        Assert.Equal(2, titles.Count);
        Assert.Equal("Batman: The Long Halloween", titles[0]);
        Assert.Equal("The Long Halloween", titles[1]);
    }

    [Theory]
    [InlineData("Batman:")]
    [InlineData("Batman:  ")]
    public void ResolveSearchTitles_HandlesEmptySubtitleGracefully(string input)
    {
        var titles = SeriesAliasResolver.ResolveSearchTitles(input);
        Assert.Single(titles);
        Assert.Equal(input.Trim(), titles[0]);
    }
}
