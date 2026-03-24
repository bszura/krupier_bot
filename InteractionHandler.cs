using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Reflection;

public class InteractionHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;

    public InteractionHandler(DiscordSocketClient client, InteractionService interactions, IServiceProvider services)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
    }

    public async Task InitializeAsync()
    {
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        _client.Ready += async () =>
        {
            // Rejestruj na konkretnym serwerze – działa natychmiast (bez opóźnienia 1h)
            var guildId = Environment.GetEnvironmentVariable("GUILD_ID");
            if (guildId != null && ulong.TryParse(guildId, out ulong id))
            {
                await _interactions.RegisterCommandsToGuildAsync(id);
                Console.WriteLine($"✅ Komendy slash zarejestrowane na serwerze {id}.");
            }
            else
            {
                await _interactions.RegisterCommandsGloballyAsync();
                Console.WriteLine("✅ Komendy slash zarejestrowane globalnie.");
            }
        };
        _client.InteractionCreated += async interaction =>
        {
            var ctx = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(ctx, _services);
        };
    }
}
