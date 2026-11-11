using DiscordBot.DTO;
using DiscordBot.Services;
using FluentAssertions;

namespace DiscordBot.Tests;

[TestFixture]
public class TUMorningBriefingLogicTests
{
    [TestCase(0, "Ciel dégagé", "☀️")]
    [TestCase(2, "Éclaircies", "⛅")]
    [TestCase(61, "Pluie faible", "🌧️")]
    [TestCase(95, "Orage", "⛈️")]
    public void GetWeatherDescription_ShouldMapWmoCodesAccurately(int code, string expectedDescription, string expectedEmoji)
    {
        // Act
        var result = OpenMeteoService.GetWeatherDescription(code);

        // Assert
        result.Description.Should().Be(expectedDescription);
        result.Emoji.Should().Be(expectedEmoji);
    }

    [Test]
    public void BuildLocalAnalysis_ShouldRecommendBike_WhenWeatherIsDryAndCalm()
    {
        // Arrange
        var aller = new CommuteSlotSummaryDto
        {
            TimeRange = "07h - 08h",
            MinTemp = 12,
            MaxTemp = 14,
            MaxRainProbability = 5,
            TotalPrecipitation = 0.0,
            MaxWindSpeed = 10,
            MaxWindGusts = 20,
            MainCondition = "Ciel dégagé",
            MainEmoji = "☀️"
        };

        var retour = new CommuteSlotSummaryDto
        {
            TimeRange = "17h - 18h",
            MinTemp = 18,
            MaxTemp = 20,
            MaxRainProbability = 10,
            TotalPrecipitation = 0.0,
            MaxWindSpeed = 15,
            MaxWindGusts = 25,
            MainCondition = "Éclaircies",
            MainEmoji = "⛅"
        };

        // Act
        var result = MorningBriefingService.BuildLocalAnalysis(aller, retour);

        // Assert
        result.Verdict.Should().Be(BikeVerdictDto.Ok);
        result.VerdictBanner.Should().Contain("Conditions idéales");
    }

    [Test]
    public void BuildLocalAnalysis_ShouldAdviseCaution_WhenModerateRainOrGusts()
    {
        // Arrange
        var aller = new CommuteSlotSummaryDto
        {
            TimeRange = "07h - 08h",
            MinTemp = 10,
            MaxTemp = 11,
            MaxRainProbability = 55,
            TotalPrecipitation = 0.5,
            MaxWindSpeed = 20,
            MaxWindGusts = 38,
            MainCondition = "Pluie faible",
            MainEmoji = "🌧️"
        };

        var retour = new CommuteSlotSummaryDto
        {
            TimeRange = "17h - 18h",
            MinTemp = 14,
            MaxTemp = 15,
            MaxRainProbability = 20,
            TotalPrecipitation = 0.1,
            MaxWindSpeed = 15,
            MaxWindGusts = 25,
            MainCondition = "Couvert",
            MainEmoji = "☁️"
        };

        // Act
        var result = MorningBriefingService.BuildLocalAnalysis(aller, retour);

        // Assert
        result.Verdict.Should().Be(BikeVerdictDto.Prudence);
        result.VerdictBanner.Should().Contain("prudence");
    }

    [Test]
    public void BuildLocalAnalysis_ShouldDiscourageBike_WhenHeavyRainOrDangerousGusts()
    {
        // Arrange
        var aller = new CommuteSlotSummaryDto
        {
            TimeRange = "07h - 08h",
            MinTemp = 8,
            MaxTemp = 9,
            MaxRainProbability = 90,
            TotalPrecipitation = 3.2,
            MaxWindSpeed = 35,
            MaxWindGusts = 58,
            MainCondition = "Pluie forte",
            MainEmoji = "🌧️"
        };

        var retour = new CommuteSlotSummaryDto
        {
            TimeRange = "17h - 18h",
            MinTemp = 11,
            MaxTemp = 12,
            MaxRainProbability = 80,
            TotalPrecipitation = 2.0,
            MaxWindSpeed = 30,
            MaxWindGusts = 52,
            MainCondition = "Pluie modérée",
            MainEmoji = "🌧️"
        };

        // Act
        var result = MorningBriefingService.BuildLocalAnalysis(aller, retour);

        // Assert
        result.Verdict.Should().Be(BikeVerdictDto.Deconseille);
        result.VerdictBanner.Should().Contain("déconseillé");
    }
}
