using Discord;
using Discord.Interactions;

namespace DiscordBot.Commands;

public class HelpModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("help", "Lista wszystkich komend kasyna")]
    public async Task Help()
    {
        var embed = new EmbedBuilder()
            .WithTitle("🎰 Kasyno – Pomoc")
            .WithColor(Color.Gold)
            .WithDescription("Wszystkie komendy dostępne na serwerze:")
            .WithThumbnailUrl("attachment://rira.png")
            .AddField("💰 Ekonomia",
                "`/portfel` – Sprawdź saldo\n" +
                "`/work` – Pracuj i zarabiaj (cooldown: 1h)\n" +
                "`/rob <gracz>` – Okradnij kogoś (cooldown: 30min)\n" +
                "`/top` – Ranking bogaczy", false)
            .AddField("🎮 Gry kasynowe",
                "`/blackjack <stawka>` – 🃏 Blackjack\n" +
                "`/bj-hit` – Dobierz kartę\n" +
                "`/bj-stand` – Zatrzymaj się", false)
            .AddField("🃏 Blackjack – zasady",
                "Dobieraj karty (`/bj-hit`) by zbliżyć się do **21**.\n" +
                "Przekroczenie 21 = **bust** (przegrana).\n" +
                "Krupier dobiera do **17**. Blackjack = **x1.5**!", false)
            .WithFooter("Waluta serwerowa: riry")
            .Build();

        var att = new FileAttachment("rira.png", "rira.png");
        await RespondWithFileAsync(attachment: att, embed: embed);
    }
}
