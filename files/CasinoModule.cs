using Discord;
using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Commands;

public class CasinoModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _db;
    private static readonly Dictionary<ulong, BlackjackGame> _games = new();

    public CasinoModule(DatabaseService db) => _db = db;

    // ── 🃏 BLACKJACK ──────────────────────────────────────────

    [SlashCommand("blackjack", "Zagraj w Blackjacka!")]
    public async Task Blackjack([Summary("stawka")] int bet)
    {
        if (bet <= 0) { await RespondAsync("❌ Stawka musi być > 0!", ephemeral: true); return; }
        var bal = await _db.GetBalanceAsync(Context.User.Id);
        if (bet > bal) { await RespondAsync($"❌ Masz tylko **{bal}**.", ephemeral: true); return; }

        if (_games.ContainsKey(Context.User.Id))
        {
            await RespondAsync("❌ Masz aktywną grę! Użyj `/bj-hit` lub `/bj-stand`.", ephemeral: true); return;
        }

        var game = new BlackjackGame(bet);
        _games[Context.User.Id] = game;
        await _db.UpdateBalanceAsync(Context.User.Id, -bet);

        if (game.PlayerTotal == 21)
        {
            _games.Remove(Context.User.Id);
            int win = (int)(bet * 1.5);
            await _db.UpdateBalanceAsync(Context.User.Id, bet + win, true);
            await Send(game, $"🃏 BLACKJACK! +{win}", Color.Gold, await _db.GetBalanceAsync(Context.User.Id));
            return;
        }
        await Send(game, "Hit czy Stand?", Color.Blue, null);
    }

    [SlashCommand("bj-hit", "Dobierz kartę")]
    public async Task Hit()
    {
        if (!_games.TryGetValue(Context.User.Id, out var game))
        {
            await RespondAsync("❌ Brak gry. Użyj `/blackjack`.", ephemeral: true); return;
        }
        game.PlayerHit();
        if (game.PlayerTotal > 21)
        {
            _games.Remove(Context.User.Id);
            await Send(game, $"💥 Bust! Przegrałeś **{game.Bet}**.", Color.Red, await _db.GetBalanceAsync(Context.User.Id));
            return;
        }
        await Send(game, $"Masz **{game.PlayerTotal}**. Hit czy Stand?", Color.Blue, null);
    }

    [SlashCommand("bj-stand", "Zatrzymaj się")]
    public async Task Stand()
    {
        if (!_games.TryGetValue(Context.User.Id, out var game))
        {
            await RespondAsync("❌ Brak gry. Użyj `/blackjack`.", ephemeral: true); return;
        }
        _games.Remove(Context.User.Id);
        game.DealerPlay();

        int payout; string msg;
        if      (game.DealerTotal > 21 || game.PlayerTotal > game.DealerTotal) (payout, msg) = (game.Bet * 2, $"🎉 Wygrałeś **{game.Bet}**!");
        else if (game.PlayerTotal == game.DealerTotal)                          (payout, msg) = (game.Bet,     "🤝 Remis – zwrot stawki.");
        else                                                                     (payout, msg) = (0,            $"💸 Przegrałeś **{game.Bet}**.");

        await _db.UpdateBalanceAsync(Context.User.Id, payout, true);
        await Send(game, msg,
            payout > game.Bet ? Color.Green : payout == game.Bet ? Color.Orange : Color.Red,
            await _db.GetBalanceAsync(Context.User.Id), showDealer: true);
    }

    private async Task Send(BlackjackGame g, string status, Color color, int? bal, bool showDealer = false)
    {
        var e = new EmbedBuilder()
            .WithTitle("🃏 Blackjack").WithColor(color).WithDescription(status)
            .AddField("Twoje karty", $"{string.Join(" ", g.PlayerCards)} → **{g.PlayerTotal}**")
            .AddField("Krupier", showDealer
                ? $"{string.Join(" ", g.DealerCards)} → **{g.DealerTotal}**"
                : $"{g.DealerCards[0]} 🂠")
            .AddField("Stawka", $"{g.Bet}", true);
        if (bal.HasValue) e.AddField("Saldo", $"{bal}", true);
        e.WithFooter("Waluta: riry");

        using var rira = File.OpenRead("rira.png");
        await RespondWithFileAsync(rira, "rira.png", embed: e.Build());
    }
}

// ── BLACKJACK LOGIKA ──────────────────────────────────────────

public class BlackjackGame
{
    private static readonly string[] Suits = ["♠","♥","♦","♣"];
    private static readonly string[] Ranks = ["A","2","3","4","5","6","7","8","9","10","J","Q","K"];
    private readonly List<(string r, string s)> _deck = [];
    private static readonly Random _rng = new();

    public List<string> PlayerCards { get; } = [];
    public List<string> DealerCards { get; } = [];
    public int Bet { get; }

    public BlackjackGame(int bet)
    {
        Bet = bet;
        foreach (var s in Suits) foreach (var r in Ranks) _deck.Add((r, s));
        for (int i = _deck.Count - 1; i > 0; i--) { int j = _rng.Next(i+1); (_deck[i],_deck[j])=(_deck[j],_deck[i]); }
        Deal(PlayerCards); Deal(DealerCards); Deal(PlayerCards); Deal(DealerCards);
    }

    private void Deal(List<string> h) { var c=_deck[^1]; _deck.RemoveAt(_deck.Count-1); h.Add($"{c.r}{c.s}"); }
    public void PlayerHit() => Deal(PlayerCards);
    public void DealerPlay() { while (Calc(DealerCards) < 17) Deal(DealerCards); }
    public int PlayerTotal => Calc(PlayerCards);
    public int DealerTotal => Calc(DealerCards);

    private static int Calc(List<string> h)
    {
        int t=0, a=0;
        foreach (var c in h)
        {
            string r = c[..^1];
            if (r == "A") { t += 11; a++; }
            else if (r is "J" or "Q" or "K") t += 10;
            else t += int.Parse(r);
        }
        while (t > 21 && a > 0) { t -= 10; a--; }
        return t;
    }
}
