from __future__ import annotations

import sqlite3
from datetime import datetime, timezone
from pathlib import Path


class Memory:
    def __init__(self, db_path: Path) -> None:
        db_path.parent.mkdir(parents=True, exist_ok=True)
        self._conn = sqlite3.connect(db_path, check_same_thread=False)
        self._conn.row_factory = sqlite3.Row
        self._conn.execute(
            """
            CREATE TABLE IF NOT EXISTS facts (
                id INTEGER PRIMARY KEY,
                key TEXT UNIQUE NOT NULL,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )
            """
        )
        self._conn.execute(
            """
            CREATE TABLE IF NOT EXISTS turns (
                id INTEGER PRIMARY KEY,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """
        )
        self._conn.commit()

    def remember(self, key: str, value: str) -> str:
        now = datetime.now(timezone.utc).isoformat()
        self._conn.execute(
            """
            INSERT INTO facts(key, value, updated_at) VALUES (?, ?, ?)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value, updated_at=excluded.updated_at
            """,
            (key.strip().lower(), value.strip(), now),
        )
        self._conn.commit()
        return f"Запомнил: {key} = {value}"

    def recall(self, query: str, limit: int = 8) -> str:
        q = f"%{query.strip().lower()}%"
        rows = self._conn.execute(
            """
            SELECT key, value FROM facts
            WHERE key LIKE ? OR value LIKE ?
            ORDER BY updated_at DESC LIMIT ?
            """,
            (q, q, limit),
        ).fetchall()
        if not rows:
            all_rows = self._conn.execute(
                "SELECT key, value FROM facts ORDER BY updated_at DESC LIMIT ?",
                (limit,),
            ).fetchall()
            if not all_rows:
                return "В памяти пока пусто."
            return "\n".join(f"- {r['key']}: {r['value']}" for r in all_rows)
        return "\n".join(f"- {r['key']}: {r['value']}" for r in rows)

    def add_turn(self, role: str, content: str) -> None:
        now = datetime.now(timezone.utc).isoformat()
        self._conn.execute(
            "INSERT INTO turns(role, content, created_at) VALUES (?, ?, ?)",
            (role, content, now),
        )
        self._conn.execute(
            """
            DELETE FROM turns WHERE id NOT IN (
                SELECT id FROM turns ORDER BY id DESC LIMIT 40
            )
            """
        )
        self._conn.commit()

    def recent_turns(self, limit: int = 16) -> list[dict[str, str]]:
        rows = self._conn.execute(
            "SELECT role, content FROM turns ORDER BY id DESC LIMIT ?",
            (limit,),
        ).fetchall()
        return [{"role": r["role"], "content": r["content"]} for r in reversed(rows)]
