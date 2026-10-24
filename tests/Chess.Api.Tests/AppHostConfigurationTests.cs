using System.Xml.Linq;
using Xunit;

namespace Chess.Api.Tests;

public class AppHostConfigurationTests
{
    [Fact]
    public void AppHostHasUserSecretsIdSoGeneratedPostgresPasswordPersists()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot(), "src", "Chess.AppHost", "Chess.AppHost.csproj"));

        var userSecretsId = project.Descendants("UserSecretsId").Select(element => element.Value.Trim()).FirstOrDefault();

        Assert.False(string.IsNullOrEmpty(userSecretsId));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chess.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Chess.sln was not found above the test binaries.");
    }
}
