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
                "`/bal` – Sprawdź portfel i bank\n" +
                "`/dep <kwota>` – Wpłać do banku\n" +
                "`/with <kwota>` – Wypłać z banku\n" +
                "`/work` – Pracuj i zarabiaj (cooldown: 1h)\n" +
                "`/rob <gracz>` – Okradnij czyjś portfel (cooldown: 30min)\n" +
                "`/top` – Ranking bogaczy", false)
            .AddField("🎮 Gry kasynowe",
                "`/roulette <zakład> <stawka>` – 🎡 Ruletka multiplayer (30s)\n" +
                "`/bj <stawka>` – 🃏 Blackjack (przyciski Hit/Stand)", false)
            .AddField("🎡 Ruletka – wypłaty",
                "🔴 Czerwony / ⚫ Czarny → **x2**\n" +
                "Parzyste / Nieparzyste / 1–18 / 19–36 → **x2**\n" +
                "Tuzin (1–12 / 13–24 / 25–36) → **x3**\n" +
                "🟢 Zielony (0) → **x18**\n" +
                "Konkretna liczba (0–36) → **x36**", false)
            .AddField("🃏 Blackjack – zasady",
                "Użyj `/bj <stawka>` żeby zacząć.\n" +
                "Kliknij **Hit** żeby dobrać kartę, **Stand** żeby się zatrzymać.\n" +
                "Zbliż się do **21** nie przekraczając. Krupier dobiera do **17**.\n" +
                "Blackjack (21 na starcie) = **x1.5**!", false)
            .AddField("🏦 Bank",
                "Pieniądze w banku są **bezpieczne** – nie można ich ukraść!\n" +
                "Używaj `/dep` żeby chować kasę przed rabusiami.", false)
            .WithFooter("Waluta serwerowa: riry")
            .Build();

        var att = new FileAttachment("rira.png", "rira.png");
        await RespondWithFileAsync(attachment: att, embed: embed);
    }
}
