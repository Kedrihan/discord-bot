using Discord;
using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public class GeneralModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly MorningBriefingService _morningBriefingService;

    public GeneralModule(MorningBriefingService morningBriefingService)
    {
        _morningBriefingService = morningBriefingService;
    }

    [SlashCommand("ping", "Vérifie si le bot répond et affiche sa latence")]
    public async Task PingAsync()
    {
        int latency = Context.Client.Latency;

        var embed = new EmbedBuilder()
            .WithTitle("🏓 Pong !")
            .WithDescription($"Le bot en **.NET 10** fonctionne parfaitement sur Docker !\n\n⏱️ **Latence :** `{latency} ms`")
            .WithColor(Color.Green)
            .WithCurrentTimestamp()
            .WithFooter(footer => footer.Text = "DiscordBot .NET")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("briefing", "Affiche le briefing météo et le verdict vélo pour aujourd'hui")]
    public async Task BriefingAsync()
    {
        await DeferAsync();

        var (success, message, embed) = await _morningBriefingService.GenerateBriefingEmbedAsync(isManualTest: false);
        if (success && embed != null)
        {
            await FollowupAsync(embed: embed);
        }
        else
        {
            await FollowupAsync($"⚠️ Impossible de générer le briefing : {message}");
        }
    }
}
