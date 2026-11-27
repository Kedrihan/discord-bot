using DiscordBot.DTO.TechWatch;
using DiscordBot.Features.TechWatch;
using FluentAssertions;

namespace DiscordBot.Tests.Features.TechWatch;

[TestFixture]
public class TUNewsAggregatorServiceTests
{
    [TestCase("en", "Announcing .NET 10 Preview 3")]
    [TestCase("en-US", "Building resilient APIs with C# 13 🚀")]
    [TestCase("fr", "Nouveautés de PostgreSQL 18 pour les développeurs")]
    [TestCase("fr-FR", "Guide complet d'architecture verticale en .NET")]
    [TestCase("", "Understanding async/await in TypeScript")]
    public void IsAllowedLanguage_ShouldAcceptEnglishAndFrenchArticles(string language, string title)
    {
        var article = new NewsArticleDto
        {
            Title = title,
            Url = "https://example.com/article",
            Language = language
        };

        NewsAggregatorService.IsAllowedLanguage(article).Should().BeTrue();
    }

    [TestCase("ja", "C# 13的新機能まとめ")]
    [TestCase("es", "Cómo crear una API con .NET 10")]
    [TestCase("es-ES", "Guía completa de Docker para desarrolladores")]
    [TestCase("pt-BR", "Como criar uma API em C#")]
    [TestCase("de", "Einführung in ASP.NET Core")]
    [TestCase("zh", "深入理解 .NET GC")]
    public void IsAllowedLanguage_ShouldRejectUnsupportedLanguageCodes(string language, string title)
    {
        var article = new NewsArticleDto
        {
            Title = title,
            Url = "https://example.com/article",
            Language = language
        };

        NewsAggregatorService.IsAllowedLanguage(article).Should().BeFalse();
    }

    [TestCase("en", "ASP.NET Coreで始めるマイクロサービス入門")]
    [TestCase("en", "Введение в асинхронное программирование на C#")]
    [TestCase("en", "TypeScript로 안전한 API 만들기")]
    [TestCase("en", "深入理解 .NET 10 性能优化")]
    public void IsAllowedLanguage_ShouldRejectNonLatinScriptTitlesEvenWhenLabeledEnglish(string language, string title)
    {
        var article = new NewsArticleDto
        {
            Title = title,
            Url = "https://example.com/article",
            Language = language
        };

        NewsAggregatorService.IsAllowedLanguage(article).Should().BeFalse();
    }
}
