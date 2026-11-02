using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Services;
using dotenv.net;

// Chargement automatique du fichier .env s'il existe
DotEnv.Load(new DotEnvOptions(ignoreExceptions: true));

var builder = WebApplication.CreateBuilder(args);

// Configuration de Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Service de paramètres et d'état du bot
builder.Services.AddSingleton<BotSettingsService>();

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

// Enregistrement du service d'arrière-plan du bot Discord
builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
