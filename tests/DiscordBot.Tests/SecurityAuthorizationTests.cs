using System.Reflection;
using DiscordBot.Components.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Tests;

[TestFixture]
public class SecurityAuthorizationTests
{
    [Test]
    public void Dashboard_ShouldHave_AdminOnly_AuthorizeAttribute_And_DashboardRoute()
    {
        var type = typeof(Dashboard);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/dashboard");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().NotBeEmpty();
        authAttrs.Should().Contain(a => a.Policy == "AdminOnly");
    }

    [Test]
    public void MorningBriefingPage_ShouldHave_AdminOnly_AuthorizeAttribute_And_BriefingRoute()
    {
        var type = typeof(MorningBriefingPage);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/briefing");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().NotBeEmpty();
        authAttrs.Should().Contain(a => a.Policy == "AdminOnly");
    }

    [Test]
    public void Settings_ShouldHave_AdminOnly_AuthorizeAttribute_And_SettingsRoute()
    {
        var type = typeof(Settings);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/settings");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().NotBeEmpty();
        authAttrs.Should().Contain(a => a.Policy == "AdminOnly");
    }

    [Test]
    public void LandingPage_ShouldBePublicRoot_And_HaveNoAuthorizeAttribute()
    {
        var type = typeof(LandingPage);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().BeEmpty("The landing page must be accessible to unauthenticated visitors.");
    }
}
