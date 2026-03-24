using Discord;
using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Commands;

public class GamesModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _db;
    private static readonly Random _rng = new();

    public GamesModule(DatabaseService db) => _db = db;

    // ── helpers ───────────────────────────────────────────────

    private async Task<int?> ParseBet(string raw)
    {
        var bal = await _db.GetBalanceAsync(Context.User.Id);
        int bet = raw.Trim().ToLower() == "all" ? bal
            : int.TryParse(raw, out int b) ? b : -1;
        if (bet <= 0) { await RespondAsync("❌ Podaj prawidłową stawkę lub 'all'.", ephemeral: true); return null; }
        if (bet > bal) { await RespondAsync($"❌ Masz tylko **{bal} 💰**.", ephemeral: true); return null; }
        return bet;
    }

    // ── 🎰 SLOTS ─────────────────────────────────────────────

    private static readonly string[] Symbols = ["🍒", "🍋", "🍊", "🍇", "⭐", "💎", "7️⃣"];
    private static readonly int[]    Weights  = [ 30,   25,   20,   15,   6,    3,    1  ];

    private static string SpinSlot()
    {
        int total = Weights.Sum();
        int r = _rng.Next(total);
        int cumul = 0;
        for (int i = 0; i < Symbols.Length; i++)
        {
            cumul += Weights[i];
            if (r < cumul) return Symbols[i];
        }
        return Symbols[0];
    }

    private static int SlotMultiplier(string a, string b, string c)
    {
        if (a == b && b == c)
        {
            return a switch
            {
                "7️⃣" => 50,
                "💎" => 20,
                "⭐" => 10,
                "🍇" => 5,
                "🍊" => 4,
                "🍋" => 3,
                "🍒" => 2,
                _ => 2
            };
        }
        if (a == b || b == c || a == c) return 0; // dwie takie same – brak wygranej
        if (a == "🍒" || b == "🍒" || c == "🍒") return 0; // pojedyncza wiśnia – brak
        return 0;
    }

    [SlashCommand("slots", "Zagraj na jednoramiennym bandycie!")]
    public async Task Slots([Summary("stawka", "Ile postawić? Wpisz 'all' żeby postawić wszystko")] string rawBet)
    {
        var bet = await ParseBet(rawBet);
        if (bet == null) return;

        await _db.UpdateBalanceAsync(Context.User.Id, -bet.Value);

        string s1 = SpinSlot(), s2 = SpinSlot(), s3 = SpinSlot();
        int mult = SlotMultiplier(s1, s2, s3);
        int payout = mult > 0 ? bet.Value * mult : 0;

        if (payout > 0) await _db.UpdateBalanceAsync(Context.User.Id, payout, true);
        var newBal = await _db.GetBalanceAsync(Context.User.Id);

        string result = mult > 0
            ? $"🎉 **Wygrana x{mult}!** +{payout - bet.Value} 💰"
            : $"💸 Przegrana! -{bet.Value} 💰";

        string jackpot = s1 == s2 && s2 == s3 ? "\n🚨 **JACKPOT!!!** 🚨" : "";

        var embed = new EmbedBuilder()
            .WithTitle("🎰 Slots")
            .WithColor(mult > 0 ? Color.Gold : Color.Red)
            .WithDescription($"# {s1} {s2} {s3}{jackpot}\n{result}")
            .AddField("Stawka", $"{bet.Value} 💰", true)
            .AddField("Saldo", $"{newBal} 💰", true)
            .WithFooter("3x takie same = wygrana | 7️⃣7️⃣7️⃣ = x50!")
            .Build();

        await RespondAsync(embed: embed);
    }

    // ── 🪙 COINFLIP ───────────────────────────────────────────

    [SlashCommand("coinflip", "Rzuć monetą – orzeł czy reszka?")]
    public async Task Coinflip(
        [Summary("wybor", "Orzeł czy reszka?")]
        [Choice("🦅 Orzeł", "orzel")]
        [Choice("🔤 Reszka", "reszka")]
        string choice,
        [Summary("stawka", "Ile postawić? Wpisz 'all' żeby postawić wszystko")] string rawBet)
    {
        var bet = await ParseBet(rawBet);
        if (bet == null) return;

        await _db.UpdateBalanceAsync(Context.User.Id, -bet.Value);

        bool isOrzel = _rng.Next(2) == 0;
        string result = isOrzel ? "orzel" : "reszka";
        string emoji  = isOrzel ? "🦅" : "🔤";
        bool won = result == choice;

        if (won) await _db.UpdateBalanceAsync(Context.User.Id, bet.Value * 2, true);
        var newBal = await _db.GetBalanceAsync(Context.User.Id);

        var embed = new EmbedBuilder()
            .WithTitle("🪙 Coinflip")
            .WithColor(won ? Color.Green : Color.Red)
            .WithDescription($"# {emoji}\nWypadło: **{(isOrzel ? "Orzeł" : "Reszka")}**\n" +
                (won ? $"🎉 Wygrałeś **{bet.Value} 💰**!" : $"💸 Przegrałeś **{bet.Value} 💰**!"))
            .AddField("Twój wybór", choice == "orzel" ? "🦅 Orzeł" : "🔤 Reszka", true)
            .AddField("Stawka", $"{bet.Value} 💰", true)
            .AddField("Saldo", $"{newBal} 💰", true)
            .Build();

        await RespondAsync(embed: embed);
    }

    // ── 🎲 DICE ───────────────────────────────────────────────

    [SlashCommand("dice", "Rzuć kostką – wyżej czy niżej niż 3.5?")]
    public async Task Dice(
        [Summary("wybor", "Obstawiasz niskie (1-3) czy wysokie (4-6)?")]
        [Choice("📉 Niskie 1–3 (x2)", "low")]
        [Choice("📈 Wysokie 4–6 (x2)", "high")]
        [Choice("🎯 Konkretna liczba (x5)", "exact")]
        string choice,
        [Summary("stawka", "Ile postawić? Wpisz 'all' żeby postawić wszystko")] string rawBet,
        [Summary("liczba", "Podaj liczbę 1-6 jeśli wybrałeś 'Konkretna liczba'")] int? number = null)
    {
        if (choice == "exact" && (number == null || number < 1 || number > 6))
        {
            await RespondAsync("❌ Podaj liczbę 1–6 przy wyborze 'Konkretna liczba'.", ephemeral: true);
            return;
        }

        var bet = await ParseBet(rawBet);
        if (bet == null) return;

        await _db.UpdateBalanceAsync(Context.User.Id, -bet.Value);

        int roll = _rng.Next(1, 7);
        string diceEmoji = roll switch { 1 => "⚀", 2 => "⚁", 3 => "⚂", 4 => "⚃", 5 => "⚄", 6 => "⚅", _ => "🎲" };

        bool won = choice switch
        {
            "low"   => roll <= 3,
            "high"  => roll >= 4,
            "exact" => roll == number,
            _ => false
        };

        int mult   = choice == "exact" ? 5 : 2;
        int payout = won ? bet.Value * mult : 0;

        if (won) await _db.UpdateBalanceAsync(Context.User.Id, payout, true);
        var newBal = await _db.GetBalanceAsync(Context.User.Id);

        string choiceText = choice switch
        {
            "low"   => "📉 Niskie (1–3)",
            "high"  => "📈 Wysokie (4–6)",
            "exact" => $"🎯 Liczba {number}",
            _ => choice
        };

        var embed = new EmbedBuilder()
            .WithTitle("🎲 Dice")
            .WithColor(won ? Color.Green : Color.Red)
            .WithDescription($"# {diceEmoji}\nWypadło: **{roll}**\n" +
                (won ? $"🎉 Wygrałeś **{payout - bet.Value} 💰**! (x{mult})" : $"💸 Przegrałeś **{bet.Value} 💰**!"))
            .AddField("Twój wybór", choiceText, true)
            .AddField("Stawka", $"{bet.Value} 💰", true)
            .AddField("Saldo", $"{newBal} 💰", true)
            .Build();

        await RespondAsync(embed: embed);
    }
}
