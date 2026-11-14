using System.Reflection;
using DiscordBot.Components.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUYouTubePageTests
{
    [Test]
    public void YouTubePage_ShouldHave_AdminOnly_AuthorizeAttribute_And_YouTubeRoute()
    {
        var type = typeof(YouTubePage);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/youtube");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().NotBeEmpty();
        authAttrs.Should().Contain(a => a.Policy == "AdminOnly");
    }
}
