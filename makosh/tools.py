from __future__ import annotations

import os
import shutil
import subprocess
import webbrowser
from pathlib import Path

import pyautogui

from makosh import config

pyautogui.FAILSAFE = True

SAFE_ROOTS = [
    Path.home(),
    config.DATA_DIR,
    Path.home() / "Desktop",
    Path.home() / "Downloads",
    Path.home() / "Documents",
]


def _safe_path(raw: str) -> Path:
    path = Path(os.path.expandvars(raw)).expanduser().resolve()
    for root in SAFE_ROOTS:
        try:
            path.relative_to(root.resolve())
            return path
        except ValueError:
            continue
    raise ValueError(f"Путь вне разрешённых папок: {path}")


def list_files(raw: str) -> str:
    path = _safe_path(raw or str(Path.home() / "Downloads"))
    if not path.exists():
        return f"Нет такого пути: {path}"
    if path.is_file():
        return str(path)
    names = sorted(path.iterdir(), key=lambda p: p.name.lower())[:80]
    if not names:
        return f"Папка пустая: {path}"
    lines = [f"{'📁' if p.is_dir() else '📄'} {p.name}" for p in names]
    return f"{path}\n" + "\n".join(lines)


def open_path(raw: str) -> str:
    path = _safe_path(raw)
    if not path.exists():
        return f"Не найдено: {path}"
    os.startfile(path)  # type: ignore[attr-defined]
    return f"Открыл {path}"


def open_app(name: str) -> str:
    key = name.strip().lower()
    mapped = config.ALLOWED_APPS.get(key, key)
    denied = f"Приложение «{name}» не в белом списке. Можно: блокнот, проводник, калькулятор, браузер."
    if mapped is None or mapped == "https://":
        webbrowser.open("https://google.com")
        return "Открыл браузер"
    if str(mapped).startswith("http"):
        webbrowser.open(mapped)
        return f"Открыл {mapped}"
    if key.startswith("http://") or key.startswith("https://"):
        webbrowser.open(name.strip())
        return f"Открыл {name}"
    # Имя с путём не должно проходить по basename, например ..\notepad.
    if any(ch in str(mapped) for ch in ("\\", "/", ":")):
        return denied
    allowed_bins = {"explorer", "notepad", "calc", "mspaint", "cmd"}
    target = Path(mapped).name if "\\" in str(mapped) else str(mapped)
    if target.lower() not in allowed_bins and key not in config.ALLOWED_APPS:
        return denied
    subprocess.Popen(mapped, shell=False)
    return f"Запустил {mapped}"


def type_text(text: str) -> str:
    pyautogui.typewrite(text, interval=0.02)
    return "Напечатал текст"


def press_hotkey(keys: str) -> str:
    parts = [k.strip().lower() for k in keys.replace("+", " ").split() if k.strip()]
    allowed = {
        "ctrl",
        "alt",
        "shift",
        "win",
        "tab",
        "enter",
        "esc",
        "space",
        "backspace",
        "delete",
        "up",
        "down",
        "left",
        "right",
        "c",
        "v",
        "x",
        "z",
        "a",
        "s",
        "f",
        "n",
        "w",
        "t",
        "l",
    }
    if not parts or any(p not in allowed for p in parts):
        return f"Клавиши не разрешены: {keys}"
    pyautogui.hotkey(*parts)
    return f"Нажал {keys}"


def copy_into_inbox(src: str, device_id: str) -> Path:
    path = _safe_path(src)
    if not path.is_file():
        raise ValueError(f"Это не файл: {path}")
    dest_dir = config.INBOX_DIR / device_id
    dest_dir.mkdir(parents=True, exist_ok=True)
    dest = dest_dir / path.name
    shutil.copy2(path, dest)
    return dest
