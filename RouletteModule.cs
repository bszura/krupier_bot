using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using DColor = Discord.Color;
using SColor = SixLabors.ImageSharp.Color;

namespace DiscordBot.Commands;

public record RouletteBet(ulong UserId, string Username, string AvatarUrl, string Key, int Amount);

public class RouletteRound
{
    public ulong ChannelId { get; init; }
    public List<RouletteBet> Bets { get; } = [];
    public ulong? MessageId { get; set; }
    public bool Done { get; set; }
    public DateTime EndsAt { get; init; } = DateTime.UtcNow.AddSeconds(30);
}

public static class RoundStore
{
    private static readonly Dictionary<ulong, RouletteRound> _map = new();
    private static readonly object _lock = new();
    public static RouletteRound? Get(ulong ch) { lock (_lock) { return _map.TryGetValue(ch, out var r) ? r : null; } }
    public static void Set(ulong ch, RouletteRound r) { lock (_lock) { _map[ch] = r; } }
    public static void Remove(ulong ch) { lock (_lock) { _map.Remove(ch); } }
}

public class RouletteModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _db;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly HashSet<int> Reds = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];

    // Pozycje środków pól na planszy 400x190px
    private static readonly int[][] NumberRows =
    [
        [3,6,9,12,15,18,21,24,27,30,33,36],
        [2,5,8,11,14,17,20,23,26,29,32,35],
        [1,4,7,10,13,16,19,22,25,28,31,34],
    ];
    private const int GX = 2, GY = 2, CW = 30, CH = 40;

    private static readonly Dictionary<string, (int x, int y)> FieldCenters = new()
    {
        ["low"]   = (30,  171), ["even"]  = (90,  171), ["red"]   = (148, 171),
        ["black"] = (210, 171), ["odd"]   = (272, 171), ["high"]  = (332, 171),
        ["d1"]    = (60,  137), ["d2"]    = (181, 137), ["d3"]    = (302, 137),
        ["green"] = (381, 137),
        ["row1"] = (382, 100), ["row2"] = (382, 60), ["row3"] = (382, 20),
    };

    private static (int x, int y) GetCenter(string key)
    {
        if (FieldCenters.TryGetValue(key, out var c)) return c;
        if (key.StartsWith('n') && int.TryParse(key[1..], out int n))
            for (int ri = 0; ri < NumberRows.Length; ri++)
            {
                int ci = Array.IndexOf(NumberRows[ri], n);
                if (ci >= 0) return (GX + ci * CW + CW / 2, GY + ri * CH + CH / 2);
            }
        return (0, 0);
    }

    public RouletteModule(DatabaseService db) => _db = db;

    private static string Label(string k) => k switch
    {
        "red" => "czerwonym", "black" => "czarnym", "green" => "zielonym (0)",
        "even" => "parzystym", "odd" => "nieparzystym",
        "low" => "1–18", "high" => "19–36",
        "d1" => "1. tuzinie", "d2" => "2. tuzinie", "d3" => "3. tuzinie",
        "row1" => "1st rzędzie", "row2" => "2nd rzędzie", "row3" => "3rd rzędzie",
        _ when k.StartsWith('n') => $"numerze {k[1..]}",
        _ => k
    };

    [SlashCommand("roulette", "Postaw zakład na ruletce! Losowanie po 30s.")]
    public async Task Roulette(
        [Summary("zaklad")]
        [Choice("🔴 Czerwony (x2)",    "red")]
        [Choice("⚫ Czarny (x2)",      "black")]
        [Choice("🟢 Zielony 0 (x18)", "green")]
        [Choice("Parzyste (x2)",       "even")]
        [Choice("Nieparzyste (x2)",    "odd")]
        [Choice("1–18 (x2)",           "low")]
        [Choice("19–36 (x2)",          "high")]
        [Choice("Tuzin 1–12 (x3)",   "d1")]
        [Choice("Tuzin 13–24 (x3)",  "d2")]
        [Choice("Tuzin 25–36 (x3)",  "d3")]
        [Choice("1st rząd (x3)",     "row1")]
        [Choice("2nd rząd (x3)",     "row2")]
        [Choice("3rd rząd (x3)",     "row3")]
        string zaklad,
        [Summary("stawka", "Ile postawić? Wpisz 'all' żeby postawić wszystko")] string rawStawka,
        [Summary("liczba", "Konkretna liczba 0–36 (x36)")] int? liczba = null)
    {
        if (liczba.HasValue)
        {
            if (liczba < 0 || liczba > 36) { await RespondAsync("❌ Liczba 0–36.", ephemeral: true); return; }
            zaklad = liczba == 0 ? "green" : "n" + liczba;
        }

        var bal = await _db.GetBalanceAsync(Context.User.Id);
        int stawka = rawStawka.Trim().ToLower() == "all" ? bal
            : int.TryParse(rawStawka, out int s) ? s : -1;
        if (stawka <= 0) { await RespondAsync("❌ Podaj prawidłową stawkę lub 'all'.", ephemeral: true); return; }
        if (stawka > bal) { await RespondAsync($"❌ Masz tylko **{bal}**.", ephemeral: true); return; }

        var ch = Context.Channel.Id;
        var round = RoundStore.Get(ch);

        if (round?.Done == true) { await RespondAsync("⏳ Runda się kończy.", ephemeral: true); return; }
        if (round?.Bets.Any(b => b.UserId == Context.User.Id && b.Key == zaklad) == true)
        { await RespondAsync("❌ Już postawiłeś na to pole!", ephemeral: true); return; }

        await _db.UpdateBalanceAsync(Context.User.Id, -stawka);
        var avatarUrl = Context.User.GetAvatarUrl(ImageFormat.Png, 64) ?? Context.User.GetDefaultAvatarUrl();
        var bet = new RouletteBet(Context.User.Id, Context.User.Username, avatarUrl, zaklad, stawka);

        if (round == null)
        {
            round = new RouletteRound { ChannelId = ch };
            round.Bets.Add(bet);
            RoundStore.Set(ch, round);

            await RespondAsync($"✅ Zakład **{Label(zaklad)}** za **{stawka}** przyjęty!", ephemeral: true);

            var capturedRound = round;
            var capturedDb = _db;
            var capturedChannel = Context.Channel;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var board = await GenerateBoard(capturedRound.Bets);
                    var msg = await capturedChannel.SendFileAsync(board, "ruletka.png",
                        embed: BuildEmbed(capturedRound.Bets, 30));
                    capturedRound.MessageId = msg.Id;

                    await Task.Delay(10_000);
                    using var b2 = await GenerateBoard(capturedRound.Bets);
                    await msg.ModifyAsync(p => { p.Embed = BuildEmbed(capturedRound.Bets, 20); p.Attachments = new List<FileAttachment> { new(b2, "ruletka.png") }; });

                    await Task.Delay(10_000);
                    using var b3 = await GenerateBoard(capturedRound.Bets);
                    await msg.ModifyAsync(p => { p.Embed = BuildEmbed(capturedRound.Bets, 10); p.Attachments = new List<FileAttachment> { new(b3, "ruletka.png") }; });

                    await Task.Delay(10_000);
                    capturedRound.Done = true;
                    await Spin(capturedRound, capturedDb, capturedChannel, msg);
                }
                catch (Exception ex) { Console.WriteLine($"[ROULETTE ERR] {ex}"); }
            });
        }
        else
        {
            round.Bets.Add(bet);
            await RespondAsync($"✅ Zakład **{Label(zaklad)}** za **{stawka}** przyjęty!", ephemeral: true);

            if (round.MessageId.HasValue && Context.Channel is ITextChannel tc)
            {
                try
                {
                    var msg = await tc.GetMessageAsync(round.MessageId.Value) as IUserMessage;
                    if (msg != null)
                    {
                        int secs = Math.Max(0, (int)(round.EndsAt - DateTime.UtcNow).TotalSeconds);
                        using var board = await GenerateBoard(round.Bets);
                        await msg.ModifyAsync(p =>
                        {
                            p.Embed = BuildEmbed(round.Bets, secs);
                            p.Attachments = new List<FileAttachment> { new(board, "ruletka.png") };
                        });
                    }
                }
                catch { }
            }
        }
    }

    // ── Generowanie obrazka ───────────────────────────────────

    private static async Task<Stream> GenerateBoard(List<RouletteBet> bets)
    {
        using var board = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>("ruletka.png");

        foreach (var b in bets)
        {
            var (cx, cy) = GetCenter(b.Key);
            if (cx == 0 && cy == 0) continue;

            try
            {
                var bytes = await _http.GetByteArrayAsync(b.AvatarUrl);
                using var av = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes);

                const int size = 22;
                av.Mutate(x => x.Resize(size, size));

                // Przytnij do koła
                var circle = new EllipsePolygon(size / 2f, size / 2f, size / 2f);
                av.Mutate(x => x
                    .SetGraphicsOptions(new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.DestIn })
                    .Fill(SColor.White, circle));

                // Naklej na planszę
                board.Mutate(x => x.DrawImage(av, new SixLabors.ImageSharp.Point(cx - size / 2, cy - size / 2), 1f));
            }
            catch { /* avatar niedostępny */ }
        }

        var ms = new MemoryStream();
        await board.SaveAsync(ms, new PngEncoder());
        ms.Position = 0;
        return ms;
    }

    // ── Embed ─────────────────────────────────────────────────

    private static Embed BuildEmbed(List<RouletteBet> bets, int secs)
    {
        var eb = new EmbedBuilder()
            .WithTitle("🎡 Ruletka")
            .WithColor(new DColor(0, 160, 60))
            .WithImageUrl("attachment://ruletka.png")
            .WithDescription($"⏳ Zakłady otwarte! Losowanie za **{secs} sek.**");

        foreach (var b in bets)
            eb.AddField($"👤 {b.Username}", $"{Label(b.Key)} – **{b.Amount}**", inline: true);

        eb.WithFooter("Waluta: riry");
        return eb.Build();
    }

    // ── Losowanie ─────────────────────────────────────────────

    private static async Task Spin(RouletteRound round, DatabaseService db, IMessageChannel channel, IUserMessage boardMsg)
    {
        int result = new Random().Next(0, 37);
        bool isRed = Reds.Contains(result);
        bool isGreen = result == 0;
        bool isEven = result != 0 && result % 2 == 0;

        int Mult(string k) => k switch
        {
            "red"   => isRed ? 2 : 0,
            "black" => !isRed && !isGreen ? 2 : 0,
            "green" => isGreen ? 18 : 0,
            "even"  => isEven ? 2 : 0,
            "odd"   => !isEven && !isGreen ? 2 : 0,
            "low"   => result is >= 1 and <= 18 ? 2 : 0,
            "high"  => result is >= 19 and <= 36 ? 2 : 0,
            "d1"    => result is >= 1 and <= 12 ? 3 : 0,
            "d2"    => result is >= 13 and <= 24 ? 3 : 0,
            "d3"    => result is >= 25 and <= 36 ? 3 : 0,
            "row1"  => new[]{ 1,4,7,10,13,16,19,22,25,28,31,34 }.Contains(result) ? 3 : 0,
            "row2"  => new[]{ 2,5,8,11,14,17,20,23,26,29,32,35 }.Contains(result) ? 3 : 0,
            "row3"  => new[]{ 3,6,9,12,15,18,21,24,27,30,33,36 }.Contains(result) ? 3 : 0,
            _ when k == "n" + result => 36,
            _ => 0
        };

        var wins = new List<string>();
        var loss = new List<string>();
        foreach (var b in round.Bets)
        {
            int m = Mult(b.Key);
            if (m > 0) { await db.UpdateBalanceAsync(b.UserId, b.Amount * m, true); wins.Add($"<@{b.UserId}> (x{m})"); }
            else loss.Add($"<@{b.UserId}>");
        }

        string col = isGreen ? "zielonym" : isRed ? "czerwonym" : "czarnym";
        string txt = $"🎉 Piłka wylądowała na: **{col} {result}**";
        if (wins.Any()) txt += $"\n\n**Zwycięzcy:** {string.Join(", ", wins)}";
        if (loss.Any()) txt += $"\n**Przegrani:** {string.Join(", ", loss)}";
        if (!round.Bets.Any()) txt += "\nBrak zakładów.";

        await channel.SendMessageAsync(txt);
        RoundStore.Remove(round.ChannelId);
    }
}
