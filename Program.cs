using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Services;
using dotenv.net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Chargement automatique du fichier .env s'il existe
DotEnv.Load(new DotEnvOptions(ignoreExceptions: true));

var builder = Host.CreateApplicationBuilder(args);

// Configuration du client Discord
builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds,
    LogGatewayIntentWarnings = false,
    AlwaysDownloadUsers = false
});

builder.Services.AddSingleton<DiscordSocketClient>();

// Configuration du gestionnaire d'interactions (Slash Commands)
builder.Services.AddSingleton(provider => new InteractionService(
    provider.GetRequiredService<DiscordSocketClient>(),
    new InteractionServiceConfig
    {
        UseCompiledLambda = true,
        DefaultRunMode = RunMode.Async
    }
));

// Enregistrement du service d'arrière-plan du bot
builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

await app.RunAsync();
