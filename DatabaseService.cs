using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DiscordBot.Services;

public class DatabaseService
{
    private static readonly HttpClient _http = new();
    private string _url = "";
    private string _token = "";

    public async Task InitializeAsync()
    {
        _url   = Environment.GetEnvironmentVariable("TURSO_URL")
            ?? throw new Exception("Brak TURSO_URL!");
        _token = Environment.GetEnvironmentVariable("TURSO_TOKEN")
            ?? throw new Exception("Brak TURSO_TOKEN!");

        // Zamień libsql:// na https://
        _url = _url.Replace("libsql://", "https://");

        await Exec(@"CREATE TABLE IF NOT EXISTS players (
            user_id    TEXT PRIMARY KEY,
            balance    INTEGER NOT NULL DEFAULT 100,
            bank       INTEGER NOT NULL DEFAULT 0,
            last_work  TEXT,
            last_rob   TEXT,
            total_won  INTEGER NOT NULL DEFAULT 0,
            total_lost INTEGER NOT NULL DEFAULT 0
        )");

        Console.WriteLine("✅ Baza danych (Turso) gotowa.");
    }

    // ── HTTP API ──────────────────────────────────────────────

    private async Task<JsonElement> Exec(string sql, params object?[] args)
    {
        // Turso HTTP API format
        var sqlParams = args.Select(a => a == null
            ? (object)new { type = "null" }
            : a is int or long
                ? new { type = "integer", value = a.ToString() }
                : new { type = "text", value = a.ToString() }).ToArray();

        var body = JsonSerializer.Serialize(new
        {
            requests = new object[]
            {
                new { type = "execute", stmt = new { sql, args = sqlParams } },
                new { type = "close" }
            }
        });

        var req = new HttpRequestMessage(HttpMethod.Post, $"{_url}/v2/pipeline");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    private async Task<List<List<JsonElement>>> Query(string sql, params object?[] args)
    {
        var result = await Exec(sql, args);
        var rows = new List<List<JsonElement>>();

        try
        {
            Console.WriteLine($"[DB] Raw response: {result}");
            // Turso /v2/pipeline zwraca: {"results":[{"type":"ok","response":{"type":"execute","result":{"cols":[...],"rows":[...]}}}]}
            var resultRows = result
                .GetProperty("results")[0]
                .GetProperty("response")
                .GetProperty("result")
                .GetProperty("rows");
            foreach (var row in resultRows.EnumerateArray())
            {
                var r = new List<JsonElement>();
                foreach (var col in row.EnumerateArray())
                    r.Add(col);
                rows.Add(r);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DB] Query parse error: {ex.Message}"); }

        return rows;
    }

    // Turso zwraca wartości jako {"type":"integer","value":"123"} lub {"type":"text","value":"abc"} lub {"type":"null"}
    private static int GetInt(List<JsonElement> row, int col)
    {
        try
        {
            var v = row[col];
            if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
            if (v.ValueKind == JsonValueKind.Object)
            {
                if (v.TryGetProperty("value", out var val))
                {
                    if (val.ValueKind == JsonValueKind.Number) return val.GetInt32();
                    if (val.ValueKind == JsonValueKind.String) return int.TryParse(val.GetString(), out int n) ? n : 0;
                }
            }
        }
        catch { }
        return 0;
    }

    private static string? GetStr(List<JsonElement> row, int col)
    {
        try
        {
            var v = row[col];
            if (v.ValueKind == JsonValueKind.Object)
            {
                if (v.TryGetProperty("type", out var t) && t.GetString() == "null") return null;
                if (v.TryGetProperty("value", out var val)) return val.GetString();
            }
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
        }
        catch { }
        return null;
    }

    // ── Public API ────────────────────────────────────────────

    private async Task EnsurePlayerAsync(ulong userId)
    {
        await Exec("INSERT OR IGNORE INTO players (user_id) VALUES (?)", userId.ToString());
    }

    public async Task<int> GetBalanceAsync(ulong userId)
    {
        await EnsurePlayerAsync(userId);
        var rows = await Query("SELECT balance FROM players WHERE user_id = ?", userId.ToString());
        return rows.Count > 0 ? GetInt(rows[0], 0) : 0;
    }

    public async Task<(int balance, int bank)> GetWalletAsync(ulong userId)
    {
        await EnsurePlayerAsync(userId);
        var rows = await Query("SELECT balance, bank FROM players WHERE user_id = ?", userId.ToString());
        if (rows.Count > 0) return (GetInt(rows[0], 0), GetInt(rows[0], 1));
        return (0, 0);
    }

    public async Task UpdateBalanceAsync(ulong userId, int delta, bool isWin = false)
    {
        await EnsurePlayerAsync(userId);
        if (isWin)
            await Exec(@"UPDATE players SET
                balance    = MAX(0, balance + ?),
                total_won  = total_won  + CASE WHEN ? > 0 THEN ? ELSE 0 END,
                total_lost = total_lost + CASE WHEN ? < 0 THEN ABS(?) ELSE 0 END
                WHERE user_id = ?",
                delta, delta, delta, delta, delta, userId.ToString());
        else
            await Exec("UPDATE players SET balance = MAX(0, balance + ?) WHERE user_id = ?",
                delta, userId.ToString());
    }

    public async Task<bool> DepositAsync(ulong userId, int amount)
    {
        await EnsurePlayerAsync(userId);
        var rows = await Query("SELECT balance FROM players WHERE user_id = ?", userId.ToString());
        if (rows.Count == 0 || GetInt(rows[0], 0) < amount) return false;
        await Exec("UPDATE players SET balance = balance - ?, bank = bank + ? WHERE user_id = ?",
            amount, amount, userId.ToString());
        return true;
    }

    public async Task<bool> WithdrawAsync(ulong userId, int amount)
    {
        await EnsurePlayerAsync(userId);
        var rows = await Query("SELECT bank FROM players WHERE user_id = ?", userId.ToString());
        if (rows.Count == 0 || GetInt(rows[0], 0) < amount) return false;
        await Exec("UPDATE players SET bank = bank - ?, balance = balance + ? WHERE user_id = ?",
            amount, amount, userId.ToString());
        return true;
    }

    public async Task<(DateTime? lastWork, DateTime? lastRob)> GetCooldownsAsync(ulong userId)
    {
        await EnsurePlayerAsync(userId);
        var rows = await Query("SELECT last_work, last_rob FROM players WHERE user_id = ?", userId.ToString());
        if (rows.Count == 0) return (null, null);
        var lw = GetStr(rows[0], 0);
        var lr = GetStr(rows[0], 1);
        return (lw != null ? DateTime.Parse(lw) : null, lr != null ? DateTime.Parse(lr) : null);
    }

    public async Task SetCooldownAsync(ulong userId, string field)
    {
        await Exec($"UPDATE players SET {field} = ? WHERE user_id = ?",
            DateTime.UtcNow.ToString("o"), userId.ToString());
    }

    public async Task<List<(ulong userId, int balance, int bank)>> GetLeaderboardAsync(int top = 10)
    {
        var rows = await Query(
            "SELECT user_id, balance, bank FROM players ORDER BY (balance + bank) DESC LIMIT ?", top);
        return rows.Select(r => (ulong.Parse(GetStr(r, 0)!), GetInt(r, 1), GetInt(r, 2))).ToList();
    }

    public async Task<(int totalWon, int totalLost)> GetStatsAsync(ulong userId)
    {
        await EnsurePlayerAsync(userId);
        var rows = await Query("SELECT total_won, total_lost FROM players WHERE user_id = ?", userId.ToString());
        if (rows.Count > 0) return (GetInt(rows[0], 0), GetInt(rows[0], 1));
        return (0, 0);
    }
}
