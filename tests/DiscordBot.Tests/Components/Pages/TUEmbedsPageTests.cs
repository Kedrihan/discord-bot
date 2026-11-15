using System.Reflection;
using DiscordBot.Components.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Tests.Components.Pages;

[TestFixture]
public class TUEmbedsPageTests
{
    [Test]
    public void EmbedsPage_ShouldHave_AdminAccess_AuthorizeAttribute_And_EmbedsRoute()
    {
        var type = typeof(EmbedsPage);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/embeds");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().NotBeEmpty();
        authAttrs.Should().Contain(a => a.Policy == "AdminAccess");
    }
}
