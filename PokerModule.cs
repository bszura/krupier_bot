using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Services;

namespace DiscordBot.Commands;

// ── KARTY ────────────────────────────────────────────────────

public enum Suit { Spades, Hearts, Diamonds, Clubs }
public enum Rank { Two=2,Three,Four,Five,Six,Seven,Eight,Nine,Ten,Jack,Queen,King,Ace }

public record Card(Rank Rank, Suit Suit)
{
    public override string ToString() => $"{RankStr}{SuitStr}";
    private string RankStr => Rank switch
    {
        Rank.Two=>"2",Rank.Three=>"3",Rank.Four=>"4",Rank.Five=>"5",
        Rank.Six=>"6",Rank.Seven=>"7",Rank.Eight=>"8",Rank.Nine=>"9",
        Rank.Ten=>"10",Rank.Jack=>"J",Rank.Queen=>"Q",Rank.King=>"K",Rank.Ace=>"A",_=>"?"
    };
    private string SuitStr => Suit switch
    {
        Suit.Spades=>"♠",Suit.Hearts=>"♥",Suit.Diamonds=>"♦",Suit.Clubs=>"♣",_=>"?"
    };
}

public class Deck
{
    private readonly List<Card> _cards;
    private static readonly Random _rng = new();

    public Deck()
    {
        _cards = Enum.GetValues<Suit>()
            .SelectMany(s => Enum.GetValues<Rank>().Select(r => new Card(r, s)))
            .ToList();
        Shuffle();
    }

    private void Shuffle()
    {
        for (int i = _cards.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
    }

    public Card Deal() { var c = _cards[^1]; _cards.RemoveAt(_cards.Count - 1); return c; }
}

// ── OCENA RĘKI ───────────────────────────────────────────────

public enum HandRank
{
    HighCard, OnePair, TwoPair, ThreeOfAKind, Straight,
    Flush, FullHouse, FourOfAKind, StraightFlush, RoyalFlush
}

public static class HandEvaluator
{
    public static (HandRank rank, string name) Evaluate(List<Card> hole, List<Card> community)
    {
        var all = hole.Concat(community).ToList();
        var best = GetBestHand(all);
        return best;
    }

    private static (HandRank, string) GetBestHand(List<Card> cards)
    {
        var combos = GetCombinations(cards, 5);
        return combos.Select(EvaluateFive).OrderByDescending(x => (int)x.Item1).First();
    }

    private static IEnumerable<List<Card>> GetCombinations(List<Card> cards, int k)
    {
        if (k == 0) { yield return []; yield break; }
        for (int i = 0; i <= cards.Count - k; i++)
            foreach (var rest in GetCombinations(cards.Skip(i + 1).ToList(), k - 1))
                yield return new List<Card> { cards[i] }.Concat(rest).ToList();
    }

    private static (HandRank, string) EvaluateFive(List<Card> cards)
    {
        var ranks  = cards.Select(c => (int)c.Rank).OrderByDescending(r => r).ToList();
        var suits  = cards.Select(c => c.Suit).ToList();
        bool flush  = suits.Distinct().Count() == 1;
        bool straight = ranks.Zip(ranks.Skip(1), (a, b) => a - b).All(d => d == 1)
            || ranks.SequenceEqual([14,5,4,3,2]);

        var groups = ranks.GroupBy(r => r).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToList();

        if (flush && straight && ranks[0] == 14 && ranks[1] == 13) return (HandRank.RoyalFlush,    "Royal Flush");
        if (flush && straight)                                       return (HandRank.StraightFlush, "Straight Flush");
        if (groups[0].Count() == 4)                                  return (HandRank.FourOfAKind,   "Four of a Kind");
        if (groups[0].Count() == 3 && groups[1].Count() == 2)       return (HandRank.FullHouse,     "Full House");
        if (flush)                                                   return (HandRank.Flush,         "Flush");
        if (straight)                                                return (HandRank.Straight,      "Straight");
        if (groups[0].Count() == 3)                                  return (HandRank.ThreeOfAKind,  "Three of a Kind");
        if (groups[0].Count() == 2 && groups[1].Count() == 2)       return (HandRank.TwoPair,       "Two Pair");
        if (groups[0].Count() == 2)                                  return (HandRank.OnePair,       "One Pair");
        return (HandRank.HighCard, "High Card");
    }
}

// ── MODEL GRY ────────────────────────────────────────────────

public enum GamePhase { Waiting, PreFlop, Flop, Turn, River, Showdown }
public enum PlayerAction { None, Check, Call, Raise, Fold, AllIn }

public class PokerPlayer
{
    public ulong UserId   { get; init; }
    public string Username { get; init; } = "";
    public int Chips      { get; set; }
    public List<Card> Hand { get; } = [];
    public bool Folded    { get; set; }
    public bool AllIn     { get; set; }
    public int CurrentBet { get; set; }
    public bool HasActed  { get; set; }
}

public class PokerGame
{
    public string Id           { get; } = Guid.NewGuid().ToString("N")[..8];
    public ulong ChannelId     { get; init; }
    public List<PokerPlayer> Players { get; } = [];
    public GamePhase Phase     { get; set; } = GamePhase.PreFlop;
    public List<Card> Community { get; } = [];
    public int Pot             { get; set; }
    public int CurrentBet      { get; set; }
    public int DealerIndex     { get; set; }
    public int CurrentIndex    { get; set; }
    public Deck Deck           { get; } = new();
    public IUserMessage? Message { get; set; }
    public int SmallBlind      { get; init; } = 10;
    public int BigBlind        => SmallBlind * 2;

    public PokerPlayer? CurrentPlayer =>
        Players.Count > 0 ? Players[CurrentIndex % Players.Count] : null;

    public List<PokerPlayer> ActivePlayers =>
        Players.Where(p => !p.Folded && !p.AllIn).ToList();
}

// ── TURNIEJ ──────────────────────────────────────────────────

public class PokerTournament
{
    public ulong GuildId      { get; init; }
    public ulong ChannelId    { get; init; }
    public ulong AdminId      { get; init; }
    public int StartingChips  { get; init; }
    public List<PokerPlayer> Players { get; } = [];
    public List<PokerGame> ActiveGames { get; } = [];
    public bool Started       { get; set; }
    public IUserMessage? LobbyMessage { get; set; }
}

// ── STORE ────────────────────────────────────────────────────

public static class PokerStore
{
    private static readonly Dictionary<ulong, PokerTournament> _tournaments = new();
    private static readonly Dictionary<string, PokerGame> _games = new();
    private static readonly object _lock = new();

    public static PokerTournament? GetTournament(ulong channelId)
    { lock(_lock) { return _tournaments.TryGetValue(channelId, out var t) ? t : null; } }

    public static void SetTournament(ulong channelId, PokerTournament t)
    { lock(_lock) { _tournaments[channelId] = t; } }

    public static void RemoveTournament(ulong channelId)
    { lock(_lock) { _tournaments.Remove(channelId); } }

    public static PokerGame? GetGame(string id)
    { lock(_lock) { return _games.TryGetValue(id, out var g) ? g : null; } }

    public static void SetGame(PokerGame g)
    { lock(_lock) { _games[g.Id] = g; } }

    public static void RemoveGame(string id)
    { lock(_lock) { _games.Remove(id); } }

    // Znajdź grę gracza
    public static PokerGame? FindPlayerGame(ulong userId)
    { lock(_lock) { return _games.Values.FirstOrDefault(g => g.Players.Any(p => p.UserId == userId)); } }
}

// ── MODUŁ ────────────────────────────────────────────────────

public class PokerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _db;
    private static readonly Random _rng = new();

    public PokerModule(DatabaseService db) => _db = db;

    // ── /poker start ─────────────────────────────────────────

    [SlashCommand("poker", "Zarządzaj turniejem pokera")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task Poker(
        [Summary("akcja")]
        [Choice("Rozpocznij turniej", "start")]
        [Choice("Zakończ turniej",    "end")]
        string action,
        [Summary("zetony", "Liczba żetonów startowych (domyślnie 1000)")] int chips = 1000)
    {
        var ch = Context.Channel.Id;

        if (action == "end")
        {
            PokerStore.RemoveTournament(ch);
            await RespondAsync("✅ Turniej zakończony.", ephemeral: true);
            return;
        }

        // start
        if (PokerStore.GetTournament(ch) != null)
        {
            await RespondAsync("❌ Turniej już trwa na tym kanale!", ephemeral: true);
            return;
        }

        var tournament = new PokerTournament
        {
            GuildId       = Context.Guild.Id,
            ChannelId     = ch,
            AdminId       = Context.User.Id,
            StartingChips = chips
        };
        PokerStore.SetTournament(ch, tournament);

        var embed = BuildLobbyEmbed(tournament);
        var components = new ComponentBuilder()
            .WithButton("Dołącz do turnieju", "poker_join", ButtonStyle.Success, new Emoji("🃏"))
            .WithButton("Rozpocznij", "poker_begin", ButtonStyle.Primary, new Emoji("▶️"))
            .Build();

        await RespondAsync(embed: embed, components: components);
        tournament.LobbyMessage = await Context.Interaction.GetOriginalResponseAsync();
    }

    // ── Przycisk: Dołącz ─────────────────────────────────────

    [ComponentInteraction("poker_join")]
    public async Task OnJoin()
    {
        var tournament = PokerStore.GetTournament(Context.Channel.Id);
        if (tournament == null) { await RespondAsync("❌ Brak aktywnego turnieju.", ephemeral: true); return; }
        if (tournament.Started)  { await RespondAsync("❌ Turniej już się rozpoczął.", ephemeral: true); return; }
        if (tournament.Players.Any(p => p.UserId == Context.User.Id))
        { await RespondAsync("❌ Już jesteś zapisany!", ephemeral: true); return; }

        tournament.Players.Add(new PokerPlayer
        {
            UserId   = Context.User.Id,
            Username = Context.User.Username,
            Chips    = tournament.StartingChips
        });

        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
            p.Embed = BuildLobbyEmbed(tournament));

        await FollowupAsync($"✅ {Context.User.Username} dołączył do turnieju!", ephemeral: true);
    }

    // ── Przycisk: Rozpocznij ─────────────────────────────────

    [ComponentInteraction("poker_begin")]
    public async Task OnBegin()
    {
        var tournament = PokerStore.GetTournament(Context.Channel.Id);
        if (tournament == null) { await RespondAsync("❌ Brak turnieju.", ephemeral: true); return; }
        if (Context.User.Id != tournament.AdminId) { await RespondAsync("❌ Tylko admin może rozpocząć.", ephemeral: true); return; }
        if (tournament.Players.Count < 2) { await RespondAsync("❌ Potrzeba minimum 2 graczy.", ephemeral: true); return; }
        if (tournament.Started) { await RespondAsync("❌ Turniej już trwa.", ephemeral: true); return; }

        tournament.Started = true;

        // Usuń przyciski z lobby
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed = BuildLobbyEmbed(tournament, started: true);
            p.Components = new ComponentBuilder().Build();
        });

        // Podziel graczy na stoły max 4 osoby
        var shuffled = tournament.Players.OrderBy(_ => _rng.Next()).ToList();
        var tables   = shuffled.Chunk(4).ToList();

        foreach (var table in tables)
        {
            var game = new PokerGame { ChannelId = Context.Channel.Id };
            foreach (var p in table) game.Players.Add(p);
            PokerStore.SetGame(game);
            tournament.ActiveGames.Add(game);
            _ = Task.Run(() => StartGame(game, Context.Channel));
        }
    }

    // ── Logika gry ───────────────────────────────────────────

    private static async Task StartGame(PokerGame game, IMessageChannel channel)
    {
        try
        {
            DealCards(game);
            PostBlinds(game);
            game.Message = await channel.SendMessageAsync(
                embed: BuildGameEmbed(game),
                components: BuildGameButtons(game));
            PokerStore.SetGame(game);
        }
        catch (Exception ex) { Console.WriteLine($"[POKER] StartGame error: {ex.Message}"); }
    }

    private static void DealCards(PokerGame game)
    {
        foreach (var p in game.Players)
        {
            p.Hand.Clear();
            p.Hand.Add(game.Deck.Deal());
            p.Hand.Add(game.Deck.Deal());
            p.Folded = false;
            p.AllIn  = false;
            p.CurrentBet = 0;
            p.HasActed   = false;
        }
    }

    private static void PostBlinds(PokerGame game)
    {
        int sb = (game.DealerIndex + 1) % game.Players.Count;
        int bb = (game.DealerIndex + 2) % game.Players.Count;

        PlaceBet(game, game.Players[sb], game.SmallBlind);
        PlaceBet(game, game.Players[bb], game.BigBlind);
        game.CurrentBet  = game.BigBlind;
        game.CurrentIndex = (game.DealerIndex + 3) % game.Players.Count;
    }

    private static void PlaceBet(PokerGame game, PokerPlayer player, int amount)
    {
        amount = Math.Min(amount, player.Chips);
        player.Chips      -= amount;
        player.CurrentBet += amount;
        game.Pot          += amount;
        if (player.Chips == 0) player.AllIn = true;
    }

    // ── Przyciski akcji ──────────────────────────────────────

    [ComponentInteraction("poker_check:*")]
    public async Task OnCheck(string gameId)
    {
        var (game, player, error) = ValidateAction(gameId);
        if (error != null) { await RespondAsync(error, ephemeral: true); return; }

        player!.HasActed = true;
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed      = BuildGameEmbed(game!);
            p.Components = BuildGameButtons(game!);
        });
        await AdvanceGame(game!, Context.Channel);
    }

    [ComponentInteraction("poker_call:*")]
    public async Task OnCall(string gameId)
    {
        var (game, player, error) = ValidateAction(gameId);
        if (error != null) { await RespondAsync(error, ephemeral: true); return; }

        int toCall = game!.CurrentBet - player!.CurrentBet;
        PlaceBet(game, player, toCall);
        player.HasActed = true;

        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed      = BuildGameEmbed(game);
            p.Components = BuildGameButtons(game);
        });
        await AdvanceGame(game, Context.Channel);
    }

    [ComponentInteraction("poker_fold:*")]
    public async Task OnFold(string gameId)
    {
        var (game, player, error) = ValidateAction(gameId);
        if (error != null) { await RespondAsync(error, ephemeral: true); return; }

        player!.Folded  = true;
        player.HasActed = true;

        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed      = BuildGameEmbed(game!);
            p.Components = BuildGameButtons(game!);
        });
        await AdvanceGame(game!, Context.Channel);
    }

    [ComponentInteraction("poker_raise:*")]
    public async Task OnRaise(string gameId)
    {
        var (game, player, error) = ValidateAction(gameId);
        if (error != null) { await RespondAsync(error, ephemeral: true); return; }

        await Context.Interaction.RespondWithModalAsync<RaiseModal>($"poker_raise_modal:{gameId}");
    }

    [ModalInteraction("poker_raise_modal:*")]
    public async Task OnRaiseModal(string gameId, RaiseModal modal)
    {
        var game   = PokerStore.GetGame(gameId);
        var player = game?.Players.FirstOrDefault(p => p.UserId == Context.User.Id);
        if (game == null || player == null) { await RespondAsync("❌ Błąd.", ephemeral: true); return; }

        if (!int.TryParse(modal.Amount, out int raise) || raise <= 0)
        { await RespondAsync("❌ Podaj prawidłową kwotę.", ephemeral: true); return; }

        int total = game.CurrentBet - player.CurrentBet + raise;
        if (total > player.Chips) total = player.Chips;

        // Zresetuj HasActed innych graczy
        foreach (var p in game.ActivePlayers.Where(p => p.UserId != player.UserId))
            p.HasActed = false;

        PlaceBet(game, player, total);
        game.CurrentBet = player.CurrentBet;
        player.HasActed = true;

        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Embed      = BuildGameEmbed(game);
            p.Components = BuildGameButtons(game);
        });
        await AdvanceGame(game, Context.Channel);
    }

    // ── Postęp gry ───────────────────────────────────────────

    private static async Task AdvanceGame(PokerGame game, IMessageChannel channel)
    {
        try
        {
            var active = game.Players.Where(p => !p.Folded).ToList();

            // Jeden gracz zostaje – wygrywa
            if (active.Count == 1)
            {
                active[0].Chips += game.Pot;
                await EndHand(game, channel, active);
                return;
            }

            // Znajdź następnego aktywnego gracza
            bool allActed = game.ActivePlayers.All(p => p.HasActed && p.CurrentBet == game.CurrentBet);

            if (allActed)
            {
                await NextPhase(game, channel);
                return;
            }

            // Następna tura
            do
            {
                game.CurrentIndex = (game.CurrentIndex + 1) % game.Players.Count;
            }
            while (game.Players[game.CurrentIndex].Folded || game.Players[game.CurrentIndex].AllIn);

            if (game.Message != null)
                await game.Message.ModifyAsync(p =>
                {
                    p.Embed      = BuildGameEmbed(game);
                    p.Components = BuildGameButtons(game);
                });
        }
        catch (Exception ex) { Console.WriteLine($"[POKER] AdvanceGame error: {ex.Message}"); }
    }

    private static async Task NextPhase(PokerGame game, IMessageChannel channel)
    {
        // Resetuj zakłady
        foreach (var p in game.Players) { p.CurrentBet = 0; p.HasActed = false; }
        game.CurrentBet   = 0;
        game.CurrentIndex = (game.DealerIndex + 1) % game.Players.Count;
        // Pomiń folded/allin
        while (game.Players[game.CurrentIndex].Folded || game.Players[game.CurrentIndex].AllIn)
            game.CurrentIndex = (game.CurrentIndex + 1) % game.Players.Count;

        switch (game.Phase)
        {
            case GamePhase.PreFlop:
                game.Community.Add(game.Deck.Deal());
                game.Community.Add(game.Deck.Deal());
                game.Community.Add(game.Deck.Deal());
                game.Phase = GamePhase.Flop;
                break;
            case GamePhase.Flop:
                game.Community.Add(game.Deck.Deal());
                game.Phase = GamePhase.Turn;
                break;
            case GamePhase.Turn:
                game.Community.Add(game.Deck.Deal());
                game.Phase = GamePhase.River;
                break;
            case GamePhase.River:
                game.Phase = GamePhase.Showdown;
                await Showdown(game, channel);
                return;
        }

        if (game.Message != null)
            await game.Message.ModifyAsync(p =>
            {
                p.Embed      = BuildGameEmbed(game);
                p.Components = BuildGameButtons(game);
            });
    }

    private static async Task Showdown(PokerGame game, IMessageChannel channel)
    {
        var active = game.Players.Where(p => !p.Folded).ToList();
        var results = active.Select(p =>
        {
            var (rank, name) = HandEvaluator.Evaluate(p.Hand, game.Community);
            return (player: p, rank, name);
        }).OrderByDescending(x => (int)x.rank).ToList();

        var winner = results.First();
        winner.player.Chips += game.Pot;

        await EndHand(game, channel, active, winner.player, results.Select(r => $"**{r.player.Username}**: {r.name}").ToList());
    }

    private static async Task EndHand(PokerGame game, IMessageChannel channel,
        List<PokerPlayer> active, PokerPlayer? winner = null, List<string>? handResults = null)
    {
        winner ??= active.First();
        var embed = new EmbedBuilder()
            .WithTitle($"🃏 Poker – Koniec rozdania")
            .WithColor(Color.Gold)
            .WithDescription($"🏆 **{winner.Username}** wygrywa **{game.Pot} żetonów**!")
            .AddField("Karty wspólne", game.Community.Count > 0
                ? string.Join(" ", game.Community)
                : "Brak");

        if (handResults != null)
            embed.AddField("Ręce graczy", string.Join("\n", handResults));

        // Pokaż żetony
        embed.AddField("Stan żetonów",
            string.Join("\n", game.Players.Select(p =>
                $"{(p.Folded ? "❌" : "✅")} **{p.Username}**: {p.Chips} żetonów")));

        if (game.Message != null)
            await game.Message.ModifyAsync(p =>
            {
                p.Embed      = embed.Build();
                p.Components = new ComponentBuilder().Build();
            });

        // Sprawdź czy ktoś wypadł
        foreach (var p in game.Players.Where(p => p.Chips == 0).ToList())
            game.Players.Remove(p);

        PokerStore.RemoveGame(game.Id);

        // Jeśli zostało 1+ graczy – nowe rozdanie po 5s
        if (game.Players.Count >= 2)
        {
            await Task.Delay(5_000);
            var newGame = new PokerGame { ChannelId = game.ChannelId };
            foreach (var p in game.Players) newGame.Players.Add(p);
            newGame.DealerIndex = (game.DealerIndex + 1) % newGame.Players.Count;
            PokerStore.SetGame(newGame);
            await StartGame(newGame, channel);
        }
        else if (game.Players.Count == 1)
        {
            await channel.SendMessageAsync(
                $"🏆 **{game.Players[0].Username}** wygrywa turniej! Gratulacje!");
        }
    }

    // ── Walidacja ────────────────────────────────────────────

    private (PokerGame? game, PokerPlayer? player, string? error) ValidateAction(string gameId)
    {
        var game = PokerStore.GetGame(gameId);
        if (game == null) return (null, null, "❌ Gra nie istnieje.");

        var player = game.Players.FirstOrDefault(p => p.UserId == Context.User.Id);
        if (player == null) return (null, null, "❌ Nie jesteś w tej grze.");
        if (player.Folded)  return (null, null, "❌ Już spasowałeś.");

        if (game.CurrentPlayer?.UserId != Context.User.Id)
            return (null, null, "❌ Nie twoja kolej!");

        return (game, player, null);
    }

    // ── Embedy ───────────────────────────────────────────────

    private static Embed BuildLobbyEmbed(PokerTournament t, bool started = false)
    {
        var eb = new EmbedBuilder()
            .WithTitle("🃏 Turniej Pokera – Texas Hold'em")
            .WithColor(Color.Gold)
            .WithDescription(started
                ? "✅ Turniej rozpoczęty! Stoły zostały podzielone."
                : $"Kliknij **Dołącz** żeby zapisać się do turnieju!\n\n**Żetony startowe:** {t.StartingChips}")
            .AddField($"Gracze ({t.Players.Count})",
                t.Players.Count > 0
                    ? string.Join("\n", t.Players.Select(p => $"• {p.Username}"))
                    : "Brak graczy");
        return eb.Build();
    }

    private static Embed BuildGameEmbed(PokerGame game)
    {
        var current = game.CurrentPlayer;
        var community = game.Community.Count > 0
            ? string.Join(" ", game.Community)
            : "*(czekamy na flop)*";

        var eb = new EmbedBuilder()
            .WithTitle($"🃏 Poker – Stół #{game.Id}")
            .WithColor(Color.Blue)
            .WithDescription($"**Faza:** {PhaseLabel(game.Phase)}\n**Pot:** {game.Pot} żetonów\n**Aktualny zakład:** {game.CurrentBet}")
            .AddField("Karty wspólne", community);

        // Pokaż każdego gracza
        foreach (var p in game.Players)
        {
            string status = p.Folded ? "❌ Spasował" : p.AllIn ? "🔥 All-in" : $"{p.Chips} żetonów";
            bool isActive = current?.UserId == p.UserId;
            eb.AddField(
                $"{(isActive ? "👉 " : "")}{p.Username}",
                $"Zakład: {p.CurrentBet} | {status}",
                inline: true);
        }

        if (current != null && !current.Folded)
            eb.WithFooter($"Czeka na ruch: {current.Username}");

        return eb.Build();
    }

    private static MessageComponent BuildGameButtons(PokerGame game)
    {
        var current = game.CurrentPlayer;
        if (current == null || current.Folded) return new ComponentBuilder().Build();

        bool canCheck = current.CurrentBet >= game.CurrentBet;
        int toCall    = game.CurrentBet - current.CurrentBet;

        return new ComponentBuilder()
            .WithButton(canCheck ? "Check" : $"Call ({toCall})",
                canCheck ? $"poker_check:{game.Id}" : $"poker_call:{game.Id}",
                canCheck ? ButtonStyle.Secondary : ButtonStyle.Primary,
                new Emoji(canCheck ? "✅" : "📞"))
            .WithButton("Raise", $"poker_raise:{game.Id}", ButtonStyle.Success, new Emoji("📈"))
            .WithButton("Fold",  $"poker_fold:{game.Id}",  ButtonStyle.Danger,  new Emoji("🏳️"))
            .Build();
    }

    private static string PhaseLabel(GamePhase phase) => phase switch
    {
        GamePhase.PreFlop  => "Pre-Flop",
        GamePhase.Flop     => "Flop",
        GamePhase.Turn     => "Turn",
        GamePhase.River    => "River",
        GamePhase.Showdown => "Showdown",
        _ => "?"
    };
}

// ── MODAL ────────────────────────────────────────────────────

public class RaiseModal : IModal
{
    public string Title => "Podbij zakład";

    [InputLabel("Kwota podbicia")]
    [ModalTextInput("amount", TextInputStyle.Short, "np. 100")]
    public string Amount { get; set; } = "";
}
