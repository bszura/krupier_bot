using Microsoft.Data.Sqlite;

namespace DiscordBot.Services;

public class DatabaseService
{
    private const string DbPath = "casino.db";
    private readonly string _connectionString = $"Data Source={DbPath}";

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS players (
                user_id     TEXT PRIMARY KEY,
                balance     INTEGER NOT NULL DEFAULT 100,
                bank        INTEGER NOT NULL DEFAULT 0,
                last_work   TEXT,
                last_rob    TEXT,
                total_won   INTEGER NOT NULL DEFAULT 0,
                total_lost  INTEGER NOT NULL DEFAULT 0
            );";
        await cmd.ExecuteNonQueryAsync();

        // Migracja – dodaj kolumnę bank jeśli nie istnieje (dla starych baz)
        try
        {
            var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE players ADD COLUMN bank INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }
        catch { /* kolumna już istnieje */ }

        Console.WriteLine("✅ Baza danych gotowa.");
    }

    private async Task EnsurePlayerAsync(SqliteConnection conn, ulong userId)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO players (user_id) VALUES ($id);";
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> GetBalanceAsync(ulong userId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT balance FROM players WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<(int balance, int bank)> GetWalletAsync(ulong userId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT balance, bank FROM players WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$id", userId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (reader.GetInt32(0), reader.GetInt32(1));
        return (0, 0);
    }

    public async Task UpdateBalanceAsync(ulong userId, int delta, bool isWin = false)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        var cmd = conn.CreateCommand();
        if (isWin)
        {
            cmd.CommandText = @"
                UPDATE players SET 
                    balance = MAX(0, balance + $delta),
                    total_won = total_won + CASE WHEN $delta > 0 THEN $delta ELSE 0 END,
                    total_lost = total_lost + CASE WHEN $delta < 0 THEN ABS($delta) ELSE 0 END
                WHERE user_id = $id;";
        }
        else
        {
            cmd.CommandText = "UPDATE players SET balance = MAX(0, balance + $delta) WHERE user_id = $id;";
        }
        cmd.Parameters.AddWithValue("$delta", delta);
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // Przenieś z portfela do banku
    public async Task<bool> DepositAsync(ulong userId, int amount)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        // Sprawdź czy ma tyle w portfelu
        var check = conn.CreateCommand();
        check.CommandText = "SELECT balance FROM players WHERE user_id = $id;";
        check.Parameters.AddWithValue("$id", userId.ToString());
        int bal = Convert.ToInt32(await check.ExecuteScalarAsync());
        if (bal < amount) return false;

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE players 
            SET balance = balance - $amount, bank = bank + $amount
            WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$amount", amount);
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    // Przenieś z banku do portfela
    public async Task<bool> WithdrawAsync(ulong userId, int amount)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        var check = conn.CreateCommand();
        check.CommandText = "SELECT bank FROM players WHERE user_id = $id;";
        check.Parameters.AddWithValue("$id", userId.ToString());
        int bank = Convert.ToInt32(await check.ExecuteScalarAsync());
        if (bank < amount) return false;

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE players 
            SET bank = bank - $amount, balance = balance + $amount
            WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$amount", amount);
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task SetBalanceAsync(ulong userId, int amount)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE players SET balance = $amount WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$amount", amount);
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(DateTime? lastWork, DateTime? lastRob)> GetCooldownsAsync(ulong userId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_work, last_rob FROM players WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$id", userId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            DateTime? lw = reader.IsDBNull(0) ? null : DateTime.Parse(reader.GetString(0));
            DateTime? lr = reader.IsDBNull(1) ? null : DateTime.Parse(reader.GetString(1));
            return (lw, lr);
        }
        return (null, null);
    }

    public async Task SetCooldownAsync(ulong userId, string field)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE players SET {field} = $now WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<(ulong userId, int balance, int bank)>> GetLeaderboardAsync(int top = 10)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id, balance, bank FROM players ORDER BY (balance + bank) DESC LIMIT $top;";
        cmd.Parameters.AddWithValue("$top", top);

        var result = new List<(ulong, int, int)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((ulong.Parse(reader.GetString(0)), reader.GetInt32(1), reader.GetInt32(2)));
        return result;
    }

    public async Task<(int totalWon, int totalLost)> GetStatsAsync(ulong userId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await EnsurePlayerAsync(conn, userId);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT total_won, total_lost FROM players WHERE user_id = $id;";
        cmd.Parameters.AddWithValue("$id", userId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (reader.GetInt32(0), reader.GetInt32(1));
        return (0, 0);
    }
}
