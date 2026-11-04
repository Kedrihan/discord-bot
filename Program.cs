using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Services;
using dotenv.net;
using TwitchLib.EventSub.Websockets.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.HttpOverrides;

// Chargement automatique du fichier .env s'il existe
DotEnv.Load(new DotEnvOptions(ignoreExceptions: true));

var builder = WebApplication.CreateBuilder(args);

// Configuration des en-têtes transférés (nécessaire derrière Docker / Reverse Proxy)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Configuration de Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpClient();

// Configuration de l'authentification Discord OAuth2
var clientId = builder.Configuration["DISCORD_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID");
var clientSecret = builder.Configuration["DISCORD_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET");
var adminUserId = builder.Configuration["DISCORD_ADMIN_USER_ID"] ?? Environment.GetEnvironmentVariable("DISCORD_ADMIN_USER_ID") ?? "116893981526196227";

var isOAuthConfigured = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = "Discord";
})
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
});

if (isOAuthConfigured)
{
    authBuilder.AddOAuth("Discord", options =>
    {
        options.ClientId = clientId!;
        options.ClientSecret = clientSecret!;
        options.CallbackPath = new PathString("/signin-discord");

        options.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
        options.TokenEndpoint = "https://discord.com/api/oauth2/token";
        options.UserInformationEndpoint = "https://discord.com/api/users/@me";

        options.Scope.Add("identify");

        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "global_name");
        options.ClaimActions.MapJsonKey("urn:discord:username", "username");
        options.ClaimActions.MapJsonKey("urn:discord:avatar", "avatar");

        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                context.RunClaimActions(user.RootElement);

                var identity = (ClaimsIdentity)context.Principal!.Identity!;
                if (string.IsNullOrWhiteSpace(identity.Name))
                {
                    var username = identity.FindFirst("urn:discord:username")?.Value;
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Name, username));
                    }
                }
            }
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            if (!isOAuthConfigured)
            {
                return true;
            }

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(userId) && userId == adminUserId;
        });
    });
});

// Abstractions d'infrastructure (Temps et Système de fichiers)
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<System.IO.Abstractions.IFileSystem, System.IO.Abstractions.FileSystem>();

// Service d'état global du bot
builder.Services.AddSingleton<BotSettingsService>();

// Services dédiés à la Veille Technologique
builder.Services.AddSingleton<TechWatchConfigService>();
builder.Services.AddSingleton<SentArticlesHistoryService>();
builder.Services.AddSingleton<NewsAggregatorService>();
builder.Services.AddSingleton<GeminiSummarizerService>();
builder.Services.AddSingleton<TechWatchService>();
builder.Services.AddHostedService<TechWatchScheduler>();

// Services dédiés au Morning Briefing Météo / Vélo
builder.Services.AddSingleton<MorningBriefingConfigService>();
builder.Services.AddSingleton<OpenMeteoService>();
builder.Services.AddSingleton<MorningBriefingService>();
builder.Services.AddHostedService<MorningBriefingScheduler>();

// Services dédiés aux notifications Twitch Live
builder.Services.AddTwitchLibEventSubWebsockets();
builder.Services.AddSingleton<TwitchConfigService>();
builder.Services.AddSingleton<TwitchStateService>();
builder.Services.AddSingleton<TwitchApiClient>();
builder.Services.AddSingleton<TwitchLiveService>();
builder.Services.AddSingleton<TwitchEventSubService>();
builder.Services.AddHostedService<TwitchLiveScheduler>();

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

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/login", () =>
{
    if (!isOAuthConfigured)
    {
        return Results.Redirect("/?oauth=not_configured");
    }
    return Results.Challenge(new AuthenticationProperties { RedirectUri = "/dashboard" }, ["Discord"]);
});

app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
