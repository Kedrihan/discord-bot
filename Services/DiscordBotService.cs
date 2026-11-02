using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class DiscordBotService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly BotSettingsService _settingsService;
    private readonly ILogger<DiscordBotService> _logger;

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactionService,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        BotSettingsService settingsService,
        ILogger<DiscordBotService> logger)
    {
        _client = client;
        _interactionService = interactionService;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client.Log += OnLogAsync;
        _interactionService.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;

        var token = _configuration["DISCORD_TOKEN"] 
            ?? _configuration["Discord:Token"]
            ?? Environment.GetEnvironmentVariable("DISCORD_TOKEN");

        if (string.IsNullOrWhiteSpace(token) || token == "votre_token_secret_ici")
        {
            _logger.LogError("DISCORD_TOKEN est manquant ou non renseigné dans le fichier .env !");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Arrêt du bot Discord en cours...");
        await _client.StopAsync();
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

        _logger.Log(logLevel, log.Exception, "[Discord] {Message}", log.Message);
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Connecté en tant que {CurrentUser} !", _client.CurrentUser);

        // Définition de l'activité initiale
        await _client.SetGameAsync(_settingsService.Settings.BotStatusMessage);

        // Découverte et chargement des modules de commandes
        await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);

        // Enregistrement des commandes Slash
        var guildIdString = _configuration["DISCORD_GUILD_ID"] ?? Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");

        if (ulong.TryParse(guildIdString, out var guildId) && guildId > 0)
        {
            _logger.LogInformation("Enregistrement immédiat des commandes pour le serveur de test {GuildId}...", guildId);
            await _interactionService.RegisterCommandsToGuildAsync(guildId);
        }
        else
        {
            _logger.LogInformation("Enregistrement global des commandes (peut prendre jusqu'à 1 heure)...");
            await _interactionService.RegisterCommandsGloballyAsync();
        }

        _logger.LogInformation("Commandes Slash prêtes !");
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            var result = await _interactionService.ExecuteCommandAsync(context, _serviceProvider);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Erreur lors de l'exécution de la commande : {ErrorReason}", result.ErrorReason);
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    await interaction.RespondAsync($"Une erreur est survenue : `{result.ErrorReason}`", ephemeral: true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception non gérée lors d'une interaction");
        }
    }
}
