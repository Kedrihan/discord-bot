using DiscordBot.Services;
using FluentAssertions;
using Xunit;

namespace DiscordBot.Tests;

public class TechWatchServiceTests
{
    [Fact]
    public void ExtractUrls_ShouldExtractAllBracketedUrls()
    {
        // Arrange
        var message = """
            🖥️ **Veille Dev Web & IA**

            • **Nouvelle version de .NET 10 disponible**
              <https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/>
            • **Guide complet sur C# 13**
              <https://dev.to/csharp-guide>
            """;

        // Act
        var urls = TechWatchService.ExtractUrls(message);

        // Assert
        urls.Should().HaveCount(2);
        urls.Should().Contain("https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/");
        urls.Should().Contain("https://dev.to/csharp-guide");
    }

    [Fact]
    public void ExtractUrls_ShouldReturnEmpty_WhenNoUrlsInMessage()
    {
        // Arrange
        var message = "Texte sans aucun lien.";

        // Act
        var urls = TechWatchService.ExtractUrls(message);

        // Assert
        urls.Should().BeEmpty();
    }
}
