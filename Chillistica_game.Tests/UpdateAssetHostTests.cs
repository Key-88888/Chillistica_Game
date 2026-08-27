using Chillistica_game.App.Services;
using Xunit;

namespace Chillistica_game.Tests;

/// <summary>
/// The update channel only ever fetches from hosts this allowlist accepts, so a
/// host GitHub actually serves from must keep passing it. GitHub has already
/// moved release downloads once — from objects.githubusercontent.com to
/// release-assets.githubusercontent.com — and a tightened allowlist would break
/// updates silently: CheckForUpdateAsync just returns null and no banner ever
/// appears. These tests pin both ends: real GitHub hosts pass, look-alikes do not.
/// </summary>
public class UpdateAssetHostTests
{
    [Theory]
    // Verified live on 2026-08-27: a release download 302-redirects here.
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/x.zip")]
    // The URL the API actually advertises as browser_download_url.
    [InlineData("https://github.com/Key-88888/Chillistica_Game/releases/download/v0.5.0/pkg.zip")]
    // Historical asset host, and the raw/dist channel we may fall back to.
    [InlineData("https://objects.githubusercontent.com/some/object")]
    [InlineData("https://raw.githubusercontent.com/Key-88888/dist/main/pkg.zip")]
    public void AcceptsHostsGitHubActuallyServesFrom(string url)
    {
        Assert.True(UpdateCheckService.IsAllowedAssetUrl(url));
    }

    [Theory]
    // Suffix-matching done wrong would accept these; they are NOT GitHub.
    [InlineData("https://evil-githubusercontent.com/pkg.zip")]
    [InlineData("https://githubusercontent.com.attacker.net/pkg.zip")]
    [InlineData("https://notgithub.com/pkg.zip")]
    // Plain HTTP is never acceptable: the package is executable code.
    [InlineData("http://github.com/Key-88888/Chillistica_Game/releases/download/v0.5.0/pkg.zip")]
    // GitHub Pages is deliberately NOT allowed yet — adding it is a decision,
    // not an accident, because it needs matching URL-resolution changes too.
    [InlineData("https://key-88888.github.io/dist/pkg.zip")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsEverythingElse(string? url)
    {
        Assert.False(UpdateCheckService.IsAllowedAssetUrl(url));
    }
}
