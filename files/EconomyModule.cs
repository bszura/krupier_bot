using Discord;
using Discord.Interactions;
using DiscordBot.Services;

namespace DiscordBot.Commands;

public class EconomyModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _db;
    private static readonly Random _rng = new();

    private static readonly string[] WorkMessages =
    [
        "Pracowałeś jako taksówkarz i zarobiłeś **{0} 💰**!",
        "Sprzedałeś kanapki na rogu ulicy za **{0} 💰**!",
        "Znalazłeś portfel i uczciwie zwróciłeś... ale dostałeś nagrodę **{0} 💰**!",
        "Zagrałeś na gitarze w metrze i zainkasowałeś **{0} 💰**!",
        "Dostarczyłeś pizzę i napiwek wyniósł **{0} 💰**!",
        "Wygrałeś konkurs na najlepszego barmana. Nagroda: **{0} 💰**!",
        "Sprzedałeś swoje stare buty online za **{0} 💰**!"
    ];

    private static readonly string[] RobSuccessMessages =
    [
        "🦹 Wkradłeś się tylnym wejściem i ukradłeś **{0} 💰** użytkownikowi {1}!",
        "🎭 Przebrałeś się za listonosza i wyciągnąłeś **{0} 💰** od {1}!",
        "🔦 W środku nocy okradłeś {1} na **{0} 💰**!"
    ];

    public EconomyModule(DatabaseService db) => _db = db;

    [SlashCommand("bal", "Sprawdź swój lub czyjś portfel i bank")]
    public async Task Bal([Summary("gracz", "Opcjonalnie czyj portfel")] IUser? user = null)
    {
        user ??= Context.User;
        var (balance, bank) = await _db.GetWalletAsync(user.Id);

        var embed = new EmbedBuilder()
            .WithTitle($"💼 Portfel – {user.Username}")
            .WithColor(Color.Gold)
            .WithThumbnailUrl(user.GetAvatarUrl())
            .AddField("👛 Portfel", $"**{balance:N0}** 💰", true)
            .AddField("🏦 Bank", $"**{bank:N0}** 💰", true)
            .AddField("💎 Łącznie", $"**{balance + bank:N0}** 💰", true)
            .WithFooter("Pieniądze w banku są bezpieczne – nie można ich ukraść!")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("dep", "Wpłać pieniądze z portfela do banku")]
    public async Task Deposit([Summary("kwota", "Ile wpłacić?")] int amount)
    {
        if (amount <= 0) { await RespondAsync("❌ Kwota musi być > 0.", ephemeral: true); return; }

        bool ok = await _db.DepositAsync(Context.User.Id, amount);
        if (!ok)
        {
            var bal = await _db.GetBalanceAsync(Context.User.Id);
            await RespondAsync($"❌ Nie masz tyle w portfelu! Masz **{bal:N0} 💰**.", ephemeral: true);
            return;
        }

        var (balance, bank) = await _db.GetWalletAsync(Context.User.Id);
        var embed = new EmbedBuilder()
            .WithTitle("🏦 Wpłata")
            .WithColor(Color.Green)
            .WithDescription($"Wpłaciłeś **{amount:N0} 💰** do banku.")
            .AddField("👛 Portfel", $"**{balance:N0}** 💰", true)
            .AddField("🏦 Bank", $"**{bank:N0}** 💰", true)
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("with", "Wypłać pieniądze z banku do portfela")]
    public async Task Withdraw([Summary("kwota", "Ile wypłacić?")] int amount)
    {
        if (amount <= 0) { await RespondAsync("❌ Kwota musi być > 0.", ephemeral: true); return; }

        bool ok = await _db.WithdrawAsync(Context.User.Id, amount);
        if (!ok)
        {
            var (_, bank) = await _db.GetWalletAsync(Context.User.Id);
            await RespondAsync($"❌ Nie masz tyle w banku! Masz **{bank:N0} 💰**.", ephemeral: true);
            return;
        }

        var (balance, newBank) = await _db.GetWalletAsync(Context.User.Id);
        var embed = new EmbedBuilder()
            .WithTitle("💸 Wypłata")
            .WithColor(Color.Orange)
            .WithDescription($"Wypłaciłeś **{amount:N0} 💰** z banku do portfela.")
            .AddField("👛 Portfel", $"**{balance:N0}** 💰", true)
            .AddField("🏦 Bank", $"**{newBank:N0}** 💰", true)
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("work", "Idź do pracy i zarobisz trochę monet (cooldown: 1h)")]
    public async Task Work()
    {
        var (lastWork, _) = await _db.GetCooldownsAsync(Context.User.Id);
        if (lastWork.HasValue)
        {
            var remaining = lastWork.Value.AddHours(1) - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await RespondAsync($"⏳ Jesteś zmęczony! Odpoczywaj jeszcze **{remaining.Minutes}m {remaining.Seconds}s**.", ephemeral: true);
                return;
            }
        }

        int earned = _rng.Next(50, 201);
        await _db.UpdateBalanceAsync(Context.User.Id, earned);
        await _db.SetCooldownAsync(Context.User.Id, "last_work");

        var msg = WorkMessages[_rng.Next(WorkMessages.Length)];
        var (balance, bank) = await _db.GetWalletAsync(Context.User.Id);
        var embed = new EmbedBuilder()
            .WithTitle("💼 Praca")
            .WithDescription(string.Format(msg, earned))
            .WithColor(Color.Green)
            .AddField("👛 Portfel", $"**{balance:N0}** 💰", true)
            .AddField("🏦 Bank", $"**{bank:N0}** 💰", true)
            .WithFooter("Wróć za godzinę po więcej!")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("rob", "Spróbuj okraść innego gracza (tylko portfel!)")]
    public async Task Rob([Summary("ofiara", "Kogo chcesz okraść?")] IUser victim)
    {
        if (victim.Id == Context.User.Id) { await RespondAsync("🤦 Nie możesz okraść samego siebie!", ephemeral: true); return; }
        if (victim.IsBot) { await RespondAsync("🤖 Nie możesz okraść bota!", ephemeral: true); return; }

        var (_, lastRob) = await _db.GetCooldownsAsync(Context.User.Id);
        if (lastRob.HasValue)
        {
            var remaining = lastRob.Value.AddMinutes(30) - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await RespondAsync($"⏳ Policja Cię obserwuje! Odczekaj **{remaining.Minutes}m {remaining.Seconds}s**.", ephemeral: true);
                return;
            }
        }

        // Można kraść tylko z portfela, nie z banku
        var victimBalance = await _db.GetBalanceAsync(victim.Id);
        if (victimBalance < 10) { await RespondAsync($"💸 {victim.Username} nie ma nic w portfelu – nie ma czego kraść!", ephemeral: true); return; }

        await _db.SetCooldownAsync(Context.User.Id, "last_rob");

        bool success = _rng.Next(100) < 45;
        if (success)
        {
            int stolen = Math.Max(5, (int)(victimBalance * (_rng.Next(10, 31) / 100.0)));
            await _db.UpdateBalanceAsync(victim.Id, -stolen);
            await _db.UpdateBalanceAsync(Context.User.Id, stolen);

            var msg = RobSuccessMessages[_rng.Next(RobSuccessMessages.Length)];
            var embed = new EmbedBuilder()
                .WithTitle("🦹 Udany rabunek!")
                .WithDescription(string.Format(msg, stolen, victim.Mention))
                .WithColor(Color.DarkRed)
                .WithFooter("Tip: trzymaj kasę w banku – tam jest bezpieczna!")
                .Build();
            await RespondAsync(embed: embed);
        }
        else
        {
            int fine = _rng.Next(30, 101);
            await _db.UpdateBalanceAsync(Context.User.Id, -fine);

            var embed = new EmbedBuilder()
                .WithTitle("👮 Złapany!")
                .WithDescription($"Policja przyłapała Cię na gorącym uczynku!\nZapłaciłeś grzywnę **{fine} 💰**.")
                .WithColor(Color.Blue)
                .Build();
            await RespondAsync(embed: embed);
        }
    }

    [SlashCommand("top", "Ranking najbogatszych graczy")]
    public async Task Top()
    {
        var leaders = await _db.GetLeaderboardAsync(10);
        if (leaders.Count == 0) { await RespondAsync("Brak graczy w bazie!", ephemeral: true); return; }

        var medals = new[] { "🥇", "🥈", "🥉" };
        var desc = string.Join("\n", leaders.Select((l, i) =>
        {
            var medal = i < 3 ? medals[i] : $"`{i + 1}.`";
            return $"{medal} <@{l.userId}> — **{l.balance + l.bank:N0} 💰** (portfel: {l.balance:N0} | bank: {l.bank:N0})";
        }));

        var embed = new EmbedBuilder()
            .WithTitle("🏆 Ranking Kasyna")
            .WithDescription(desc)
            .WithColor(Color.Gold)
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("give", "Admin: daj monety graczowi")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task Give(IUser user, int amount)
    {
        await _db.UpdateBalanceAsync(user.Id, amount);
        var (balance, bank) = await _db.GetWalletAsync(user.Id);
        await RespondAsync($"✅ Dodano **{amount} 💰** dla {user.Mention}. Portfel: **{balance:N0}** | Bank: **{bank:N0}**", ephemeral: true);
    }
}
