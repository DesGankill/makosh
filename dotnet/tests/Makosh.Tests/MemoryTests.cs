using Makosh.Core;

namespace Makosh.Tests;

public sealed class MemoryTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "makosh-m2-" + Guid.NewGuid().ToString("N"));

    public MemoryTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    string NewDb(params string[] parts)
    {
        var segments = new List<string> { _root };
        segments.AddRange(parts.Length == 0 ? new[] { "memory.sqlite" } : parts);
        return Path.Combine(segments.ToArray());
    }

    [Fact]
    public void Creates_database_and_tables()
    {
        var path = NewDb("nested", "memory.sqlite");
        using var memory = new Memory(path);
        Assert.True(File.Exists(path));
        var tables = memory.TableNames();
        Assert.Contains("facts", tables);
        Assert.Contains("turns", tables);
    }

    [Fact]
    public void Empty_database()
    {
        using var memory = new Memory(NewDb());
        Assert.Equal("В памяти пока пусто.", memory.Recall("anything"));
        Assert.Equal("В памяти пока пусто.", memory.Recall(""));
        Assert.Empty(memory.RecentTurns());
    }

    [Fact]
    public void Remember_recall_and_update()
    {
        using var memory = new Memory(NewDb());
        Assert.Equal("Запомнил: city = Riga", memory.Remember("city", "Riga"));
        Assert.Contains("city: Riga", memory.Recall("city"), StringComparison.Ordinal);

        Assert.Equal("Запомнил: city = Vilnius", memory.Remember("city", "Vilnius"));
        var found = memory.Recall("city");
        Assert.Contains("Vilnius", found, StringComparison.Ordinal);
        Assert.DoesNotContain("Riga", found, StringComparison.Ordinal);
        Assert.Equal(1, memory.CountFacts());
    }

    [Fact]
    public void Stores_fact_key_in_lowercase()
    {
        using var memory = new Memory(NewDb());
        memory.Remember("  City  ", " Riga ");
        Assert.Equal("city", memory.StoredKey("city"));
        Assert.Null(memory.StoredKey("City"));
        Assert.Contains("- city: Riga", memory.Recall("CITY"), StringComparison.Ordinal);
    }

    [Fact]
    public void Recall_of_unknown_query_returns_latest_facts()
    {
        using var memory = new Memory(NewDb());
        memory.Remember("editor", "cursor");
        var missed = memory.Recall("no-such-key");
        Assert.Contains("editor: cursor", missed, StringComparison.Ordinal);
        Assert.DoesNotContain("пусто", missed, StringComparison.Ordinal);
    }

    [Fact]
    public void Recent_turns_are_chronological_and_capped()
    {
        using var memory = new Memory(NewDb());
        for (var index = 0; index < 45; index++)
        {
            var role = index % 2 == 0 ? "user" : "assistant";
            memory.AddTurn(role, $"m{index}");
        }

        var stored = memory.RecentTurns(limit: 100);
        Assert.Equal(40, stored.Count);
        Assert.Equal(new Turn("assistant", "m5"), stored[0]);
        Assert.Equal(new Turn("user", "m44"), stored[^1]);

        var recent = memory.RecentTurns();
        Assert.Equal(Enumerable.Range(29, 16).Select(index => $"m{index}"), recent.Select(row => row.Content));
        Assert.Equal("assistant", recent[0].Role);
        Assert.Equal("user", recent[^1].Role);
    }
}
