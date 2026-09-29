using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Makosh.Core;

/// <summary>
/// SQLite facts and chat turns. Schema and SQL match <c>makosh/memory.py</c>.
/// </summary>
public sealed class Memory : IDisposable
{
    public const int MaxStoredTurns = 40;
    public const int DefaultRecentTurns = 16;
    public const int DefaultRecallLimit = 8;

    readonly SqliteConnection _connection;
    readonly object _gate = new();
    bool _disposed;

    public Memory(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        var fullPath = Path.GetFullPath(dbPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _connection.Open();
        using (var busy = _connection.CreateCommand())
        {
            busy.CommandText = "PRAGMA busy_timeout=5000;";
            busy.ExecuteNonQuery();
        }

        lock (_gate)
        {
            Execute(
                """
                CREATE TABLE IF NOT EXISTS facts (
                    id INTEGER PRIMARY KEY,
                    key TEXT UNIQUE NOT NULL,
                    value TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                )
                """);
            Execute(
                """
                CREATE TABLE IF NOT EXISTS turns (
                    id INTEGER PRIMARY KEY,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at TEXT NOT NULL
                )
                """);
        }
    }

    public string Remember(string key, string value)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO facts(key, value, updated_at) VALUES ($key, $value, $updated)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value, updated_at=excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("$key", key.Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("$value", value.Trim());
            cmd.Parameters.AddWithValue("$updated", UtcNowIso());
            cmd.ExecuteNonQuery();
        }

        return $"Запомнил: {key} = {value}";
    }

    public string Recall(string query, int limit = DefaultRecallLimit)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var pattern = $"%{query.Trim().ToLowerInvariant()}%";
            var matched = QueryFacts(
                """
                SELECT key, value FROM facts
                WHERE key LIKE $q OR value LIKE $q
                ORDER BY updated_at DESC LIMIT $limit
                """,
                ("$q", pattern),
                ("$limit", limit));
            if (matched.Count > 0)
            {
                return FormatFacts(matched);
            }

            var latest = QueryFacts(
                "SELECT key, value FROM facts ORDER BY updated_at DESC LIMIT $limit",
                ("$limit", limit));
            if (latest.Count == 0)
            {
                return "В памяти пока пусто.";
            }

            return FormatFacts(latest);
        }
    }

    public void AddTurn(string role, string content)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using (var insert = _connection.CreateCommand())
            {
                insert.CommandText =
                    "INSERT INTO turns(role, content, created_at) VALUES ($role, $content, $created)";
                insert.Parameters.AddWithValue("$role", role);
                insert.Parameters.AddWithValue("$content", content);
                insert.Parameters.AddWithValue("$created", UtcNowIso());
                insert.ExecuteNonQuery();
            }

            using var trim = _connection.CreateCommand();
            trim.CommandText =
                """
                DELETE FROM turns WHERE id NOT IN (
                    SELECT id FROM turns ORDER BY id DESC LIMIT $keep
                )
                """;
            trim.Parameters.AddWithValue("$keep", MaxStoredTurns);
            trim.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Turn> RecentTurns(int limit = DefaultRecentTurns)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT role, content FROM turns ORDER BY id DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            var newestFirst = new List<Turn>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                newestFirst.Add(new Turn(reader.GetString(0), reader.GetString(1)));
            }

            newestFirst.Reverse();
            return newestFirst;
        }
    }

    internal IReadOnlySet<string> TableNames()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var names = new HashSet<string>(StringComparer.Ordinal);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }
    }

    internal int CountFacts()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM facts";
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    internal string? StoredKey(string lookup)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT key FROM facts WHERE key = $key";
            cmd.Parameters.AddWithValue("$key", lookup);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connection.Dispose();
        }
    }

    void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    List<(string Key, string Value)> QueryFacts(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var rows = new List<(string, string)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    static string FormatFacts(IEnumerable<(string Key, string Value)> rows) =>
        string.Join("\n", rows.Select(row => $"- {row.Key}: {row.Value}"));

    static string UtcNowIso() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffff'+00:00'", CultureInfo.InvariantCulture);

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
