using Chess.Web.Site.Publishing;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

public class GitRemoteTests
{
    [Theory]
    [InlineData("https://github.com/konradcinkusz/llm-multi-agent-chess.git", "https://konradcinkusz.github.io/llm-multi-agent-chess/")]
    [InlineData("https://github.com/Owner/Repo", "https://owner.github.io/Repo/")]
    [InlineData("git@github.com:Owner/Repo.git", "https://owner.github.io/Repo/")]
    [InlineData("ssh://git@github.com/owner/owner.github.io.git", "https://owner.github.io/")]
    [InlineData("https://user:token@github.com/owner/repo.git", "https://owner.github.io/repo/")]
    public void A_github_remote_maps_to_its_pages_address(string remote, string expected) =>
        Assert.Equal(expected, GitRemote.PagesUrl(remote, publicUrl: null));

    [Fact]
    public void Other_hosts_have_no_derived_address_and_a_configured_one_wins()
    {
        Assert.Null(GitRemote.PagesUrl("https://gitlab.example.invalid/owner/repo.git", publicUrl: null));
        Assert.Equal("https://results.example.invalid/", GitRemote.PagesUrl("https://gitlab.example.invalid/owner/repo.git", "https://results.example.invalid/"));
    }

    [Fact]
    public void Credentials_are_removed_from_a_remote_before_it_is_shown()
    {
        Assert.Equal("https://github.com/owner/repo.git", GitRemote.Redact("https://user:token@github.com/owner/repo.git"));
        Assert.Equal("git@github.com:owner/repo.git", GitRemote.Redact("git@github.com:owner/repo.git"));
    }
}
