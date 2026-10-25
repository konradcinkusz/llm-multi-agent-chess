extern alias ChessWeb;

using Chess.Web.Site;
using Chess.Web.Site.Publishing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

public class SitePublishingRegistrationTests
{
    [Fact]
    public void The_app_resolves_the_git_publisher_and_the_null_exporter_until_the_real_exporter_is_registered()
    {
        using var app = new WebApplicationFactory<ChessWeb::Program>();
        using var scope = app.Services.CreateScope();

        Assert.IsType<GitSitePublisher>(scope.ServiceProvider.GetRequiredService<ISitePublisher>());
        Assert.IsType<NullSiteExporter>(scope.ServiceProvider.GetRequiredService<ISiteExporter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SiteExportCoordinator>());
    }
}
