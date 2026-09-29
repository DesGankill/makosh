from makosh.memory import Memory


def test_creates_database_and_tables(tmp_path):
    path = tmp_path / "nested" / "memory.sqlite"
    memory = Memory(path)
    assert path.is_file()
    tables = {
        row[0]
        for row in memory._conn.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table'"
        )
    }
    assert {"facts", "turns"} <= tables


def test_empty_database(tmp_path):
    memory = Memory(tmp_path / "memory.sqlite")
    assert memory.recall("anything") == "В памяти пока пусто."
    assert memory.recall("") == "В памяти пока пусто."
    assert memory.recent_turns() == []


def test_remember_recall_and_update(tmp_path):
    memory = Memory(tmp_path / "memory.sqlite")
    assert memory.remember("city", "Riga") == "Запомнил: city = Riga"
    assert "city: Riga" in memory.recall("city")

    assert memory.remember("city", "Vilnius") == "Запомнил: city = Vilnius"
    found = memory.recall("city")
    assert "Vilnius" in found
    assert "Riga" not in found
    count = memory._conn.execute("SELECT COUNT(*) FROM facts").fetchone()[0]
    assert count == 1


def test_recall_of_unknown_query_returns_latest_facts(tmp_path):
    memory = Memory(tmp_path / "memory.sqlite")
    memory.remember("editor", "cursor")
    missed = memory.recall("no-such-key")
    assert "editor: cursor" in missed
    assert "пусто" not in missed


def test_recent_turns_are_chronological_and_capped(tmp_path):
    memory = Memory(tmp_path / "memory.sqlite")
    for index in range(45):
        role = "user" if index % 2 == 0 else "assistant"
        memory.add_turn(role, f"m{index}")

    stored = memory.recent_turns(limit=100)
    assert len(stored) == 40
    assert stored[0] == {"role": "assistant", "content": "m5"}
    assert stored[-1] == {"role": "user", "content": "m44"}

    recent = memory.recent_turns()
    assert [row["content"] for row in recent] == [f"m{index}" for index in range(29, 45)]
    assert recent[0]["role"] == "assistant"
    assert recent[-1]["role"] == "user"
