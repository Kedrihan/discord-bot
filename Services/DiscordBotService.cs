using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace DiscordBot.Services;

public class DiscordBotService(
    DiscordSocketClient client,
    InteractionService interactionService,
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DiscordBotService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += OnLogAsync;
        interactionService.Log += OnLogAsync;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;

        var token = configuration["DISCORD_TOKEN"]
            ?? configuration["Discord:Token"]
            ?? Environment.GetEnvironmentVariable("DISCORD_TOKEN");

        if (string.IsNullOrWhiteSpace(token) || token == "votre_token_secret_ici")
        {
            logger.LogError("DISCORD_TOKEN est manquant ou non renseigné dans le fichier .env !");
            return;
        }

        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Arrêt du bot Discord en cours...");
        await client.StopAsync();
    }

    private Task OnLogAsync(LogMessage log)
    {
        var logLevel = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        logger.Log(logLevel, log.Exception, "[Discord] {Message}", log.Message);
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        logger.LogInformation("Connecté en tant que {CurrentUser} !", client.CurrentUser);

        // Découverte et chargement des modules de commandes
        await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);

        // Enregistrement des commandes Slash
        var guildIdString = configuration["DISCORD_GUILD_ID"] ?? Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");

        if (ulong.TryParse(guildIdString, out var guildId) && guildId > 0)
        {
            logger.LogInformation("Enregistrement immédiat des commandes pour le serveur de test {GuildId}...", guildId);
            await interactionService.RegisterCommandsToGuildAsync(guildId);
            logger.LogInformation("Commandes Slash prêtes !");
            return;
        }

        logger.LogInformation("Enregistrement global des commandes (peut prendre jusqu'à 1 heure)...");
        await interactionService.RegisterCommandsGloballyAsync();
        logger.LogInformation("Commandes Slash prêtes !");
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(client, interaction);
            var result = await interactionService.ExecuteCommandAsync(context, serviceProvider);

            if (result.IsSuccess)
            {
                return;
            }

            logger.LogWarning("Erreur lors de l'exécution de la commande : {ErrorReason}", result.ErrorReason);
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                await interaction.RespondAsync($"Une erreur est survenue : `{result.ErrorReason}`", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception non gérée lors d'une interaction");
        }
    }
}
