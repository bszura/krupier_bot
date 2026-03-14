using Discord;
using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Commands;

public class CasinoModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _db;
    private static readonly Dictionary<ulong, BlackjackGame> _games = new();

    public CasinoModule(DatabaseService db) => _db = db;

    [SlashCommand("bj", "Zagraj w Blackjacka!")]
    public async Task Blackjack([Summary("stawka")] int bet)
    {
        if (bet <= 0) { await RespondAsync("❌ Stawka musi być > 0!", ephemeral: true); return; }
        var bal = await _db.GetBalanceAsync(Context.User.Id);
        if (bet > bal) { await RespondAsync($"❌ Masz tylko **{bal}**.", ephemeral: true); return; }
        if (_games.ContainsKey(Context.User.Id)) { await RespondAsync("❌ Masz aktywną grę!", ephemeral: true); return; }

        var game = new BlackjackGame(bet);
        _games[Context.User.Id] = game;
        await _db.UpdateBalanceAsync(Context.User.Id, -bet);

        if (game.PlayerTotal == 21)
        {
            _games.Remove(Context.User.Id);
            int win = (int)(bet * 1.5);
            await _db.UpdateBalanceAsync(Context.User.Id, bet + win, true);
            await RespondAsync(embed: BuildEmbed(game, $"🃏 BLACKJACK! +{win}", Color.Gold, await _db.GetBalanceAsync(Context.User.Id), showDealer: true));
            return;
        }

        await RespondAsync(
            embed: BuildEmbed(game, "Hit czy Stand?", Color.Blue, null),
            components: Buttons(Context.User.Id));
    }

    [ComponentInteraction("bj_hit:*")]
    public async Task OnHit(string userId)
    {
        if (Context.User.Id.ToString() != userId) { await RespondAsync("❌ To nie twoja gra!", ephemeral: true); return; }
        if (!_games.TryGetValue(Context.User.Id, out var game)) { await RespondAsync("❌ Brak gry.", ephemeral: true); return; }

        game.PlayerHit();

        if (game.PlayerTotal > 21)
        {
            _games.Remove(Context.User.Id);
            await ((Discord.WebSocket.SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
            {
                p.Embed = BuildEmbed(game, $"💥 Bust! Przegrałeś **{game.Bet}**.", Color.Red, null, showDealer: false);
                p.Components = new ComponentBuilder().Build();
            });
            return;
        }

        await ((Discord.WebSocket.SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed = BuildEmbed(game, $"Masz **{game.PlayerTotal}**. Hit czy Stand?", Color.Blue, null);
            p.Components = Buttons(Context.User.Id);
        });
    }

    [ComponentInteraction("bj_stand:*")]
    public async Task OnStand(string userId)
    {
        if (Context.User.Id.ToString() != userId) { await RespondAsync("❌ To nie twoja gra!", ephemeral: true); return; }
        if (!_games.TryGetValue(Context.User.Id, out var game)) { await RespondAsync("❌ Brak gry.", ephemeral: true); return; }

        _games.Remove(Context.User.Id);
        game.DealerPlay();

        int payout; string msg;
        if      (game.DealerTotal > 21 || game.PlayerTotal > game.DealerTotal) (payout, msg) = (game.Bet * 2, $"🎉 Wygrałeś **{game.Bet}**!");
        else if (game.PlayerTotal == game.DealerTotal)                          (payout, msg) = (game.Bet,     "🤝 Remis – zwrot stawki.");
        else                                                                     (payout, msg) = (0,            $"💸 Przegrałeś **{game.Bet}**.");

        await _db.UpdateBalanceAsync(Context.User.Id, payout, true);
        var newBal = await _db.GetBalanceAsync(Context.User.Id);

        await ((Discord.WebSocket.SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed = BuildEmbed(game, msg,
                payout > game.Bet ? Color.Green : payout == game.Bet ? Color.Orange : Color.Red,
                newBal, showDealer: true);
            p.Components = new ComponentBuilder().Build();
        });
    }

    private static MessageComponent Buttons(ulong? userId = null)
    {
        string id = userId?.ToString() ?? "0";
        return new ComponentBuilder()
            .WithButton("Hit", $"bj_hit:{id}", ButtonStyle.Primary, new Emoji("🃏"))
            .WithButton("Stand", $"bj_stand:{id}", ButtonStyle.Danger, new Emoji("✋"))
            .Build();
    }

    private static Embed BuildEmbed(BlackjackGame g, string status, Color color, int? bal, bool showDealer = false)
    {
        var dealerField = showDealer
            ? $"{string.Join(" ", g.DealerCards)} → **{g.DealerTotal}**"
            : $"{g.DealerCards[0]} 🂠 → **{g.DealerVisibleTotal}**";

        var e = new EmbedBuilder()
            .WithTitle("🃏 Blackjack")
            .WithColor(color)
            .WithDescription(status)
            .AddField("Twoje karty", $"{string.Join(" ", g.PlayerCards)} → **{g.PlayerTotal}**")
            .AddField("Krupier", dealerField)
            .AddField("Stawka", $"{g.Bet}", true);

        if (bal.HasValue) e.AddField("Saldo", $"{bal:N0}", true);
        e.WithFooter("Waluta: riry");
        return e.Build();
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
    public int DealerVisibleTotal => Calc(DealerCards.Take(1).ToList());

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
