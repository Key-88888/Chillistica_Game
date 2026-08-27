using System.IO;
using System.Security.Cryptography;
using Chillistica_game.App.Services;
using Xunit;

namespace Chillistica_game.Tests;

/// <summary>
/// Every shipped strategy declares the files it needs. WinwsEngine refuses to
/// start when one of those paths does not resolve, so a wrong path is not a
/// cosmetic defect — the engine never launches and every strategy in the ladder
/// reports failure, which reads exactly like "DPI defeated us".
///
/// That is not hypothetical: a generated catalog once wrote
/// "Engine\winws2\files\..." with single backslashes, JSON parsed \f as a form
/// feed, and the paths became garbage. The argv tests all passed, because the
/// command line itself was fine — only the file references were broken.
/// </summary>
public class StrategyFileReferenceTests
{
    [Theory]
    [InlineData("youtube")]
    [InlineData("discord")]
    [InlineData("roblox")]
    [InlineData("fortnite")]
    public void EveryDeclaredFile_ExistsAndMatchesItsPinnedHash(string appId)
    {
        AppStrategyCatalog catalog = StrategyComposer.LoadCatalog(appId);

        Assert.NotEmpty(catalog.Strategies);

        foreach (AppStrategyCandidate strategy in catalog.Strategies)
        {
            foreach (EngineFileHash file in strategy.FileHashes)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(file.Path),
                    $"{appId}/{strategy.StrategyId}: empty file path");

                // Control characters here mean the path was mangled by escaping
                // rather than merely pointing somewhere wrong.
                Assert.DoesNotContain(file.Path, c => char.IsControl(c));

                string full = Path.Combine(AppContext.BaseDirectory, file.Path);

                Assert.True(
                    File.Exists(full),
                    $"{appId}/{strategy.StrategyId}: declared file does not exist: '{file.Path}'");

                if (string.IsNullOrWhiteSpace(file.Sha256))
                {
                    continue;
                }

                using FileStream stream = File.OpenRead(full);
                string actual = Convert.ToHexString(SHA256.HashData(stream));

                Assert.True(
                    actual.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase),
                    $"{appId}/{strategy.StrategyId}: '{file.Path}' hash {actual} does not match pinned {file.Sha256}");
            }
        }
    }

    [Theory]
    [InlineData("youtube")]
    [InlineData("discord")]
    [InlineData("roblox")]
    [InlineData("fortnite")]
    public void EveryFileReferencedInArguments_IsAlsoDeclared(string appId)
    {
        AppStrategyCatalog catalog = StrategyComposer.LoadCatalog(appId);

        foreach (AppStrategyCandidate strategy in catalog.Strategies)
        {
            foreach (string token in strategy.ArgumentsFragment.Split(
                         (char[]?)null,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string value = token.Trim('"');

                int slash = value.IndexOf("files\\", StringComparison.OrdinalIgnoreCase);

                if (slash < 0)
                {
                    continue;
                }

                string fileName = Path.GetFileName(value);

                // The pre-flight check only verifies DECLARED files. A file used
                // on the command line but left undeclared skips that check and
                // surfaces as a cryptic engine failure at runtime instead.
                Assert.Contains(
                    strategy.FileHashes,
                    f => f.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
