using DiscordBot.Components.Pages;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUYouTubePageTests
{
    [Test]
    public void YouTubePage_ShouldHave_AdminAccess_AuthorizeAttribute_And_YouTubeRoute()
    {
        var type = typeof(YouTubePage);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/youtube");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().NotBeEmpty();
        authAttrs.Should().Contain(a => a.Policy == "AdminAccess");
    }
}
