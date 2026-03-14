using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using DiscordBot.Services;

// Minimalny serwer HTTP dla Render + Uptime Robot
var http = new Thread(() =>
{
    var listener = new System.Net.HttpListener();
    listener.Prefixes.Add($"http://*:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}/");
    listener.Start();
    Console.WriteLine("✅ HTTP listener gotowy.");
    while (true)
    {
        var ctx = listener.GetContext();
        var resp = ctx.Response;
        var msg = System.Text.Encoding.UTF8.GetBytes("OK");
        resp.ContentLength64 = msg.Length;
        resp.OutputStream.Write(msg, 0, msg.Length);
        resp.OutputStream.Close();
    }
});
http.IsBackground = true;
http.Start();

// Discord bot
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
