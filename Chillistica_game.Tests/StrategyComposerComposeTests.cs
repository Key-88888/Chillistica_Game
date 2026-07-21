using Xunit;

using Chillistica_game.App.Services;

namespace Chillistica_game.Tests;

public sealed class StrategyComposerComposeTests
{
    private static readonly string[] ShippedAppIds =
        ["youtube", "discord", "roblox", "fortnite"];

    [Theory]
    [InlineData("youtube")]
    [InlineData("discord")]
    [InlineData("roblox")]
    [InlineData("fortnite")]
    public void Compose_WithFirstShippedStrategy_ReturnsArguments(string appId)
    {
        StrategyComposer.ComposedProfile profile =
            StrategyComposer.Compose([(appId, 0)]);

        Assert.False(string.IsNullOrWhiteSpace(profile.Arguments));
    }

    [Fact]
    public void Compose_WithAllShippedApps_EmitsSingleHeadersAndJoinsFragments()
    {
        (string AppId, int StrategyIndex)[] selections = ShippedAppIds
            .Select(appId => (appId, 0))
            .ToArray();

        AppStrategyCandidate[] candidates = ShippedAppIds
            .Select(appId => StrategyComposer.LoadCatalog(appId).Strategies[0])
            .ToArray();

        StrategyComposer.ComposedProfile profile =
            StrategyComposer.Compose(selections);

        Assert.Equal(1, CountOccurrences(profile.Arguments, "--wf-tcp="));

        int expectedUdpHeaderCount = candidates.Any(candidate => candidate.UsesUdp)
            ? 1
            : 0;
        Assert.Equal(
            expectedUdpHeaderCount,
            CountOccurrences(profile.Arguments, "--wf-udp="));

        string expectedFragments = string.Join(
            " --new ",
            candidates.Select(candidate => candidate.ArgumentsFragment));
        Assert.EndsWith(expectedFragments, profile.Arguments);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Compose_ClampsOutOfRangeStrategyIndex(int requestedIndex)
    {
        const string appId = "youtube";
        AppStrategyCatalog catalog = StrategyComposer.LoadCatalog(appId);
        int clampedIndex = Math.Clamp(
            requestedIndex,
            0,
            catalog.Strategies.Count - 1);

        StrategyComposer.ComposedProfile expected =
            StrategyComposer.Compose([(appId, clampedIndex)]);

        StrategyComposer.ComposedProfile actual =
            StrategyComposer.Compose([(appId, requestedIndex)]);

        Assert.Equal(expected.Arguments, actual.Arguments);
        Assert.Equal(expected.RequiredFiles, actual.RequiredFiles);
    }

    [Theory]
    [InlineData("youtube", 3)]
    [InlineData("discord", 3)]
    [InlineData("roblox", 2)]
    [InlineData("fortnite", 2)]
    public void GetCandidateCount_ReturnsShippedCatalogStrategyCount(
        string appId,
        int expectedCount)
    {
        Assert.Equal(expectedCount, StrategyComposer.GetCandidateCount(appId));
    }

    [Fact]
    public void GetCandidateCount_ForUnknownApp_ReturnsOne()
    {
        Assert.Equal(1, StrategyComposer.GetCandidateCount("unknown-app"));
    }

    private static int CountOccurrences(string value, string searchValue)
    {
        int count = 0;
        int searchIndex = 0;

        while ((searchIndex = value.IndexOf(
                   searchValue,
                   searchIndex,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            searchIndex += searchValue.Length;
        }

        return count;
    }
}
