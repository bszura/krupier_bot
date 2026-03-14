using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using DiscordBot.Services;

var config = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
};

var client = new DiscordSocketClient(config);

var services = new ServiceCollection()
    .AddSingleton(client)
    .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
    .AddSingleton<DatabaseService>()
    .AddSingleton<InteractionHandler>()
    .BuildServiceProvider();

var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
    ?? throw new Exception("Brak tokenu! Ustaw DISCORD_TOKEN.");

client.Log += msg => { Console.WriteLine($"[{msg.Severity}] {msg.Source}: {msg.Message}"); return Task.CompletedTask; };

await services.GetRequiredService<DatabaseService>().InitializeAsync();
await services.GetRequiredService<InteractionHandler>().InitializeAsync();

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await client.SetActivityAsync(new Game("🎰 /roulette | /blackjack", ActivityType.Playing));

await Task.Delay(-1);
