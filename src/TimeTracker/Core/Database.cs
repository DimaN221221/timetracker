using Microsoft.Data.Sqlite;

namespace TimeTracker.Core;

/// <summary>
/// activity.db — one row per stretch of time in one window:
/// spans(id, start, end, exe, app, title), start/end in Unix seconds.
/// </summary>
internal static class Database
{
    public static SqliteConnection Open()
    {
        Paths.EnsureDataDir();
        var db = new SqliteConnection($"Data Source={Paths.Database};Default Timeout=10");
        db.Open();
        Execute(db, "PRAGMA journal_mode=WAL"); // the UI reads while the tracker thread writes
        Execute(db, """
            CREATE TABLE IF NOT EXISTS spans(
                id INTEGER PRIMARY KEY, start REAL NOT NULL, "end" REAL NOT NULL,
                exe TEXT NOT NULL, app TEXT NOT NULL, title TEXT NOT NULL)
            """);
        Execute(db, "CREATE INDEX IF NOT EXISTS spans_start ON spans(start)");
        return db;
    }

    public static void Execute(SqliteConnection db, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = Command(db, sql, args);
        cmd.ExecuteNonQuery();
    }

    public static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
}

internal static class Unix
{
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public static double From(DateTime local) => (local.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

    public static double From(DateOnly day) => From(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));

    public static DateTime ToLocal(double seconds) => DateTime.UnixEpoch.AddSeconds(seconds).ToLocalTime();
}
