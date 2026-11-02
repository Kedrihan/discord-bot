using Discord;
using Discord.Interactions;

namespace DiscordBot.Modules;

public class GeneralModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("ping", "Vérifie si le bot répond et affiche sa latence")]
    public async Task PingAsync()
    {
        int latency = Context.Client.Latency;

        var embed = new EmbedBuilder()
            .WithTitle("🏓 Pong !")
            .WithDescription($"Le bot en **.NET** fonctionne parfaitement sur Docker !\n\n⏱️ **Latence :** `{latency} ms`")
            .WithColor(Color.Green)
            .WithCurrentTimestamp()
            .WithFooter(footer => footer.Text = "Hébergé sur Oracle Cloud Always Free")
            .Build();

        await RespondAsync(embed: embed);
    }
}
