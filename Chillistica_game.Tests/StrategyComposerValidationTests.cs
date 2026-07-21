using System.Text.Json;

using Xunit;

using Chillistica_game.App.Services;

namespace Chillistica_game.Tests;

public sealed class StrategyComposerValidationTests
{
    [Theory]
    [InlineData("-wf-save=x")]
    [InlineData("-daemon")]
    [InlineData("-hostlist-auto-debug=y")]
    public void ValidateArgumentsFragment_RejectsSingleDashLongOptionAliases(
        string fragment)
    {
        Assert.Throws<InvalidOperationException>(
            () => StrategyComposer.ValidateArgumentsFragment(fragment, "test"));
    }

    [Theory]
    [InlineData("@C:\\evil\\opts.txt")]
    [InlineData("$opts")]
    [InlineData("\"@C:\\evil\\opts.txt\"")]
    [InlineData("\"$opts\"")]
    [InlineData("'@C:\\evil\\opts.txt'")]
    [InlineData("'$opts'")]
    public void ValidateArgumentsFragment_RejectsDirectAndQuotedOptionFileLoading(
        string fragment)
    {
        Assert.Throws<InvalidOperationException>(
            () => StrategyComposer.ValidateArgumentsFragment(fragment, "test"));
    }

    [Theory]
    [InlineData("\"--wf-save=x\"")]
    [InlineData("'--daemon'")]
    public void ValidateArgumentsFragment_RejectsQuotedForbiddenTokens(
        string fragment)
    {
        Assert.Throws<InvalidOperationException>(
            () => StrategyComposer.ValidateArgumentsFragment(fragment, "test"));
    }

    [Theory]
    [InlineData("--totally-unknown-flag=1")]
    [InlineData("--wf-raw-part=x")]
    public void ValidateArgumentsFragment_RejectsUnknownFlags(string fragment)
    {
        Assert.Throws<InvalidOperationException>(
            () => StrategyComposer.ValidateArgumentsFragment(fragment, "test"));
    }

    [Theory]
    [InlineData("evil.exe")]
    [InlineData("somevalue")]
    public void ValidateArgumentsFragment_RejectsBareNonFlagTokens(string fragment)
    {
        Assert.Throws<InvalidOperationException>(
            () => StrategyComposer.ValidateArgumentsFragment(fragment, "test"));
    }

    [Theory]
    [InlineData("--hostlist=C:\\Windows\\x.txt")]
    [InlineData("--hostlist=\\\\server\\share\\x")]
    [InlineData("--hostlist=%APPDATA%\\x")]
    [InlineData("--hostlist=..\\..\\x")]
    public void ValidateArgumentsFragment_RejectsDisallowedValuePaths(
        string fragment)
    {
        Assert.Throws<InvalidOperationException>(
            () => StrategyComposer.ValidateArgumentsFragment(fragment, "test"));
    }

    [Theory]
    [MemberData(nameof(ShippedArgumentsFragments))]
    public void ValidateArgumentsFragment_AcceptsEveryShippedStrategy(
        string appId,
        string strategyId,
        string fragment)
    {
        Exception? exception = Record.Exception(
            () => StrategyComposer.ValidateArgumentsFragment(fragment, appId));

        Assert.True(
            exception is null,
            $"Shipped strategy '{appId}/{strategyId}' was rejected: {exception}");
    }

    [Fact]
    public void ValidateArgumentsFragment_AcceptsNewSeparatorToken()
    {
        Exception? exception = Record.Exception(
            () => StrategyComposer.ValidateArgumentsFragment("--new", "test"));

        Assert.Null(exception);
    }

    public static IEnumerable<object[]> ShippedArgumentsFragments()
    {
        string catalogDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Engine",
            "winws2",
            "strategies");

        foreach (string catalogPath in Directory
                     .EnumerateFiles(catalogDirectory, "*.json")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            using JsonDocument document =
                JsonDocument.Parse(File.ReadAllText(catalogPath));

            string appId = document.RootElement
                .GetProperty("AppId")
                .GetString()
                ?? throw new InvalidDataException(
                    $"Catalog '{catalogPath}' has no AppId.");

            foreach (JsonElement strategy in document.RootElement
                         .GetProperty("Strategies")
                         .EnumerateArray())
            {
                string strategyId = strategy
                    .GetProperty("StrategyId")
                    .GetString()
                    ?? throw new InvalidDataException(
                        $"Catalog '{catalogPath}' has a strategy with no StrategyId.");

                string fragment = strategy
                    .GetProperty("ArgumentsFragment")
                    .GetString()
                    ?? throw new InvalidDataException(
                        $"Strategy '{strategyId}' has no ArgumentsFragment.");

                yield return new object[] { appId, strategyId, fragment };
            }
        }
    }
}
