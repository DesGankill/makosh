from __future__ import annotations

import os
import re
import shutil
import subprocess
import time
import webbrowser
from dataclasses import dataclass
from difflib import SequenceMatcher
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


def _start_menu_roots() -> list[Path]:
    roots = [
        Path.home() / "AppData" / "Roaming" / "Microsoft" / "Windows" / "Start Menu" / "Programs",
    ]
    programdata = os.environ.get("PROGRAMDATA")
    if programdata:
        roots.append(Path(programdata) / "Microsoft" / "Windows" / "Start Menu" / "Programs")
    return roots


_RU_LAT = str.maketrans(
    {
        "а": "a",
        "б": "b",
        "в": "v",
        "г": "g",
        "д": "d",
        "е": "e",
        "ё": "e",
        "ж": "z",
        "з": "z",
        "и": "i",
        "й": "y",
        "к": "k",
        "л": "l",
        "м": "m",
        "н": "n",
        "о": "o",
        "п": "p",
        "р": "r",
        "с": "s",
        "т": "t",
        "у": "u",
        "ф": "f",
        "х": "h",
        "ц": "c",
        "ч": "c",
        "ш": "s",
        "щ": "s",
        "ъ": None,
        "ы": "y",
        "ь": None,
        "э": "e",
        "ю": "u",
        "я": "a",
    }
)

STEAM_GAMES = {
    "valheim": 892970,
}


def _latin(text: str) -> str:
    return text.lower().translate(_RU_LAT)


def _compact(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", _latin(text))


def _similar(a: str, b: str) -> float:
    left, right = _latin(a), _latin(b)
    compact_a, compact_b = _compact(a), _compact(b)
    cons_a, cons_b = re.sub(r"[aeiouy]", "", compact_a), re.sub(r"[aeiouy]", "", compact_b)
    return max(
        SequenceMatcher(None, left, right).ratio(),
        SequenceMatcher(None, compact_a, compact_b).ratio(),
        SequenceMatcher(None, cons_a, cons_b).ratio() if cons_a and cons_b else 0.0,
    )


@dataclass
class _Launch:
    title: str
    names: tuple[str, ...]
    target: str
    kind: str
    exe: str | None = None


_CATALOG: tuple[float, list[_Launch]] | None = None


def _steam_libraries() -> list[Path]:
    roots: list[Path] = []
    guesses = [
        Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")) / "Steam",
        Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Steam",
    ]
    for steam in guesses:
        apps = steam / "steamapps"
        if apps.exists():
            roots.append(apps)
            vdf = apps / "libraryfolders.vdf"
            if vdf.exists():
                text = vdf.read_text(encoding="utf-8", errors="ignore")
                for match in re.finditer(r'"path"\s+"([^"]+)"', text):
                    extra = Path(match.group(1).replace("\\\\", "\\"))
                    extra_apps = extra / "steamapps"
                    if extra_apps.exists():
                        roots.append(extra_apps)
    unique: list[Path] = []
    seen: set[str] = set()
    for root in roots:
        key = str(root).lower()
        if key not in seen:
            seen.add(key)
            unique.append(root)
    return unique


def _guess_exe(apps: Path, installdir: str, title: str) -> str | None:
    folder = apps / "common" / installdir
    if not folder.is_dir():
        return None
    skip = {
        "unitycrashhandler64.exe",
        "unitycrashhandler32.exe",
        "crashhandler.exe",
        "installer.exe",
        "unins000.exe",
    }
    exes = [p for p in folder.glob("*.exe") if p.name.lower() not in skip]
    if not exes:
        return None
    want = _compact(title)
    for path in exes:
        if want and (_compact(path.stem) == want or want in _compact(path.stem) or _compact(path.stem) in want):
            return path.name
    exes.sort(key=lambda p: len(p.name))
    return exes[0].name


def _steam_games() -> list[_Launch]:
    skip = {"steamworks common redistributables", "steam linux runtime"}
    games: list[_Launch] = []
    for apps in _steam_libraries():
        for acf in apps.glob("appmanifest_*.acf"):
            try:
                text = acf.read_text(encoding="utf-8", errors="ignore")
            except OSError:
                continue
            appid = re.search(r'"appid"\s+"(\d+)"', text)
            name = re.search(r'"name"\s+"([^"]+)"', text)
            install = re.search(r'"installdir"\s+"([^"]+)"', text)
            if not appid or not name:
                continue
            title = name.group(1).strip()
            if title.lower() in skip:
                continue
            names = [title]
            if install:
                names.append(install.group(1))
            exe = _guess_exe(apps, install.group(1), title) if install else None
            games.append(
                _Launch(
                    title=title,
                    names=tuple(names),
                    target=appid.group(1),
                    kind="steam",
                    exe=exe,
                )
            )
    return games


def _shortcut_items() -> list[_Launch]:
    items: list[_Launch] = []
    files: list[Path] = []
    for root in _start_menu_roots():
        if root.exists():
            files.extend(root.rglob("*.lnk"))
            files.extend(root.rglob("*.url"))
    desktop = Path.home() / "Desktop"
    if desktop.exists():
        files.extend(desktop.glob("*.lnk"))
        files.extend(desktop.glob("*.url"))
    seen: set[str] = set()
    for path in files:
        key = str(path).lower()
        if key in seen:
            continue
        seen.add(key)
        items.append(
            _Launch(title=path.stem, names=(path.stem,), target=str(path), kind="file")
        )
    return items


def _catalog() -> list[_Launch]:
    global _CATALOG
    now = time.time()
    if _CATALOG and now - _CATALOG[0] < 90:
        return _CATALOG[1]
    items = _steam_games() + _shortcut_items()
    _CATALOG = (now, items)
    return items


def _item_score(query: str, item: _Launch) -> float:
    needle = query.strip().lower()
    best = 0.0
    compact_q = _compact(needle)
    for name in item.names:
        n = name.lower()
        compact_n = _compact(n)
        if needle == n or compact_q == compact_n:
            return 1.0
        if len(compact_q) >= 4 and compact_q in compact_n:
            best = max(best, 0.93)
        best = max(best, _similar(needle, n))
    return best


def _best_launch(query: str) -> _Launch | None:
    needle = query.strip().lower()
    if len(_compact(needle)) < 3 or needle in {"steam", "стим"}:
        return None
    ranked = sorted(
        ((_item_score(needle, item), item) for item in _catalog()),
        key=lambda pair: -pair[0],
    )
    if not ranked:
        return None
    best, item = ranked[0]
    second = ranked[1][0] if len(ranked) > 1 else 0.0
    if best >= 0.68 or (best >= 0.52 and best - second >= 0.05):
        return item
    return None


def _run_launch(item: _Launch) -> str:
    if item.kind == "steam":
        os.startfile(f"steam://rungameid/{item.target}")  # type: ignore[attr-defined]
        return f"Запустил {item.title} через Steam"
    os.startfile(item.target)  # type: ignore[attr-defined]
    return f"Запустил {item.title}"


def _find_shortcut(query: str) -> Path | None:
    needle = query.strip().lower()
    if not needle:
        return None
    hits: list[Path] = []
    candidates: list[Path] = []
    for root in _start_menu_roots():
        if root.exists():
            candidates.extend(root.rglob("*.lnk"))
            candidates.extend(root.rglob("*.url"))
    desktop = Path.home() / "Desktop"
    if desktop.exists():
        candidates.extend(desktop.glob("*.lnk"))
        candidates.extend(desktop.glob("*.url"))
    for path in candidates:
        name = path.stem.lower()
        if needle in name or name in needle or _latin(needle) in _latin(name):
            hits.append(path)
            continue
        if _similar(needle, name) >= 0.62:
            hits.append(path)
    if not hits:
        return None
    hits.sort(key=lambda p: (-_similar(needle, p.stem.lower()), len(p.stem)))
    return hits[0]


def _blender_exe() -> Path | None:
    bases = [
        Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Blender Foundation",
        Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")) / "Blender Foundation",
    ]
    found: list[Path] = []
    for base in bases:
        if base.exists():
            found.extend(base.rglob("blender.exe"))
    if not found:
        return None
    found.sort(key=lambda p: p.as_posix(), reverse=True)
    return found[0]


def _known_exe(query: str) -> Path | None:
    if query in {"blender", "блендер"}:
        return _blender_exe()
    aliases = {
        "steam": [
            Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")) / "Steam" / "steam.exe",
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Steam" / "steam.exe",
            Path.home() / "AppData" / "Local" / "Steam" / "steam.exe",
        ],
        "discord": [
            Path.home() / "AppData" / "Local" / "Discord" / "Update.exe",
            Path.home() / "AppData" / "Roaming" / "Microsoft" / "Windows" / "Start Menu" / "Programs" / "Discord Inc" / "Discord.lnk",
        ],
        "telegram": [
            Path.home() / "AppData" / "Roaming" / "Telegram Desktop" / "Telegram.exe",
        ],
        "spotify": [
            Path.home() / "AppData" / "Roaming" / "Spotify" / "Spotify.exe",
        ],
        "chrome": [
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Google" / "Chrome" / "Application" / "chrome.exe",
            Path.home() / "AppData" / "Local" / "Google" / "Chrome" / "Application" / "chrome.exe",
        ],
        "msedge": [
            Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")) / "Microsoft" / "Edge" / "Application" / "msedge.exe",
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "Microsoft" / "Edge" / "Application" / "msedge.exe",
        ],
    }
    for path in aliases.get(query, []):
        if path.exists():
            return path
    return None


def _deepseek_script() -> Path:
    return config.ROOT / "scripts" / "start-deepseek-edge.ps1"


def is_deepseek_launcher(name: str) -> bool:
    compact = _compact(name).replace("strat", "start")
    low = name.lower().replace("ё", "е")
    return (
        "deepseek" in compact
        or "dipseek" in compact
        or "dipsik" in compact
        or "дипсик" in low
        or "deep seek" in low
        or compact.endswith("startdeepseekedgeps1")
        or "startdeepseek" in compact
    )


def start_deepseek_edge() -> str:
    script = _deepseek_script()
    if script.exists():
        subprocess.Popen(
            [
                "powershell",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(script),
            ],
            shell=False,
        )
        return f"Запустил {script.name}: отдельный Edge с chat.deepseek.com. Войди там и не закрывай окно."
    profile = config.CHROME_PROFILE
    profile.mkdir(parents=True, exist_ok=True)
    edge = _known_exe("msedge")
    if not edge:
        return "Microsoft Edge не найден."
    subprocess.Popen(
        [
            str(edge),
            "--remote-debugging-port=9222",
            f"--user-data-dir={profile}",
            "https://chat.deepseek.com",
        ],
        shell=False,
    )
    return "Открыл Edge с чатом DeepSeek для Makosh."


def open_app(name: str) -> str:
    spoken = name.strip()
    raw = resolve_app_alias(name)
    key = raw.lower()
    if is_deepseek_launcher(spoken) or is_deepseek_launcher(key):
        return start_deepseek_edge()
    blocked = {"cmd", "powershell", "pwsh", "regedit", "diskpart", "format"}
    if key in blocked:
        return f"«{raw}» запускать не буду."
    mapped = config.ALLOWED_APPS.get(key, key)
    if mapped is None or mapped == "https://":
        webbrowser.open("https://google.com")
        return "Открыл браузер"
    if str(mapped).startswith("http"):
        webbrowser.open(str(mapped))
        return f"Открыл {mapped}"
    if key.startswith("http://") or key.startswith("https://"):
        webbrowser.open(raw)
        return f"Открыл {raw}"

    if str(mapped) == "steam" or key in {"steam", "стим"}:
        exe = _known_exe("steam")
        if exe:
            if exe.suffix.lower() == ".lnk":
                os.startfile(exe)  # type: ignore[attr-defined]
            else:
                subprocess.Popen([str(exe)], shell=False)
            return "Запустил Steam"
        os.startfile("steam://open/main")  # type: ignore[attr-defined]
        return "Открыл Steam"

    if str(mapped) in {"discord", "дискорд"} or key in {"discord", "дискорд"}:
        updater = Path.home() / "AppData" / "Local" / "Discord" / "Update.exe"
        if updater.exists():
            subprocess.Popen([str(updater), "--processStart", "Discord.exe"], shell=False)
            return "Запустил Discord"

    if str(mapped) in {"blender", "блендер"} or key in {"blender", "блендер"}:
        exe = _blender_exe()
        if exe:
            subprocess.Popen([str(exe)], shell=False)
            return "Запустил Blender"

    for guess in (spoken, raw, str(mapped)):
        item = _best_launch(guess)
        if item:
            return _run_launch(item)

    builtins = {"explorer", "notepad", "calc", "mspaint"}
    target = str(mapped)
    if Path(target).name.lower().replace(".exe", "") in builtins or target in builtins:
        subprocess.Popen(target, shell=False)
        return f"Запустил {target}"
    return f"Не нашёл «{spoken}». Скажи ближе к имени ярлыка или игры в Steam."


def type_text(text: str) -> str:
    pyautogui.typewrite(text, interval=0.02)
    return "Напечатал текст"


def press_hotkey(keys: str | list[str]) -> str:
    if isinstance(keys, list):
        parts = [str(k).strip().lower() for k in keys if str(k).strip()]
    else:
        parts = [k.strip().lower() for k in str(keys).replace("+", " ").replace(",", " ").split() if k.strip()]
    aliases = {
        "volume_down": "volumedown",
        "vol_down": "volumedown",
        "volume-down": "volumedown",
        "volume_up": "volumeup",
        "vol_up": "volumeup",
        "volume_mute": "volumemute",
        "mute": "volumemute",
    }
    parts = [aliases.get(p, p) for p in parts]
    media = {"volumedown", "volumeup", "volumemute"}
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
        *media,
    }
    if not parts or any(p not in allowed for p in parts):
        return f"Клавиши не разрешены: {keys}"
    if len(parts) == 1 and parts[0] in media:
        pyautogui.press(parts[0])
        return f"Нажал {parts[0]}"
    pyautogui.hotkey(*parts)
    return f"Нажал {'+'.join(parts)}"


def _endpoint_volume():
    from ctypes import POINTER, cast

    from comtypes import CLSCTX_ALL, CoCreateInstance

    from pycaw.constants import CLSID_MMDeviceEnumerator, EDataFlow, ERole
    from pycaw.pycaw import IAudioEndpointVolume, IMMDeviceEnumerator

    enumerator = CoCreateInstance(
        CLSID_MMDeviceEnumerator,
        IMMDeviceEnumerator,
        CLSCTX_ALL,
    )
    device = enumerator.GetDefaultAudioEndpoint(
        EDataFlow.eRender.value,
        ERole.eMultimedia.value,
    )
    interface = device.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
    return cast(interface, POINTER(IAudioEndpointVolume))


def _endpoint_volume_from_speakers():
    from ctypes import POINTER, cast

    from comtypes import CLSCTX_ALL
    from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume

    speakers = AudioUtilities.GetSpeakers()
    if hasattr(speakers, "EndpointVolume"):
        return speakers.EndpointVolume
    if hasattr(speakers, "Activate"):
        interface = speakers.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
        return cast(interface, POINTER(IAudioEndpointVolume))
    inner = (
        getattr(speakers, "_device", None)
        or getattr(speakers, "device", None)
        or getattr(speakers, "dev", None)
    )
    if inner is not None and hasattr(inner, "Activate"):
        interface = inner.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
        return cast(interface, POINTER(IAudioEndpointVolume))
    raise AttributeError("не нашёл EndpointVolume у устройства")


def set_volume(percent: int | str) -> str:
    value = max(0, min(100, int(float(percent))))
    last_error = None
    volume = None
    for factory in (_endpoint_volume_from_speakers, _endpoint_volume):
        try:
            volume = factory()
            break
        except Exception as exc:  # noqa: BLE001
            last_error = exc
    if volume is None:
        raise RuntimeError(f"Не удалось достучаться до звука Windows: {last_error}")
    volume.SetMasterVolumeLevelScalar(value / 100.0, None)
    if value > 0:
        try:
            volume.SetMute(0, None)
        except Exception:
            pass
    return f"Громкость {value}%"


_PROCESS_IMAGES = {
    "steam": ["steam.exe"],
    "стим": ["steam.exe"],
    "blender": ["blender.exe"],
    "блендер": ["blender.exe"],
    "discord": ["Discord.exe"],
    "дискорд": ["Discord.exe"],
    "telegram": ["Telegram.exe"],
    "телеграм": ["Telegram.exe"],
    "spotify": ["Spotify.exe"],
    "спотифай": ["Spotify.exe"],
    "notepad": ["notepad.exe"],
    "блокнот": ["notepad.exe"],
    "chrome": ["chrome.exe"],
    "хром": ["chrome.exe"],
    "msedge": ["msedge.exe"],
    "edge": ["msedge.exe"],
    "calc": ["CalculatorApp.exe", "win32calc.exe", "calc.exe"],
    "калькулятор": ["CalculatorApp.exe", "win32calc.exe", "calc.exe"],
    "valheim": ["valheim.exe"],
    "вальхейм": ["valheim.exe"],
    "волхейн": ["valheim.exe"],
    "вольхейн": ["valheim.exe"],
}

_CLOSE_BLOCKED = {
    "explorer.exe",
    "dwm.exe",
    "winlogon.exe",
    "csrss.exe",
    "services.exe",
    "lsass.exe",
    "svchost.exe",
    "system",
}


_FILLER_CHUNKS = (
    "ещё раз",
    "еще раз",
    "повторно",
    "пожалуйста",
    "на компьютере",
    "на компе",
    "на пк",
    "снова",
    "опять",
    "давай",
    "быстро",
    "сейчас",
    "просто",
    "уже",
    "же",
    "ну",
)


def resolve_app_alias(raw: str) -> str:
    text = f" {raw.lower().strip()} "
    for chunk in _FILLER_CHUNKS:
        text = text.replace(chunk, " ")
    text = re.sub(r"\s+", " ", text).strip(" .!?")
    names = sorted(
        set(list(_PROCESS_IMAGES) + list(config.ALLOWED_APPS)),
        key=len,
        reverse=True,
    )
    for name in names:
        if not name or name in {"https://"}:
            continue
        if re.search(rf"(^| ){re.escape(str(name))}( |$)", f" {text} "):
            return str(name)
    return text


def close_app(name: str) -> str:
    spoken = name.strip()
    key = resolve_app_alias(name)
    mapped = str(config.ALLOWED_APPS.get(key, key)).lower()
    images: list[str] = []
    item = _best_launch(spoken) or _best_launch(key) or _best_launch(spoken.replace(" ", ""))
    if item:
        if item.exe:
            images.append(item.exe)
        slug = _compact(item.title)
        if slug:
            images.append(slug + ".exe")
    mapped_images = _PROCESS_IMAGES.get(key) or _PROCESS_IMAGES.get(mapped)
    if mapped_images:
        images.extend(mapped_images)
    # unique keep order
    unique: list[str] = []
    seen: set[str] = set()
    for image in images:
        low = image.lower()
        if low in seen or low in _CLOSE_BLOCKED:
            continue
        seen.add(low)
        unique.append(image)
    if not unique:
        return f"Не знаю, какой процесс закрыть для «{name}»."
    killed = []
    for image in unique:
        result = subprocess.run(
            ["taskkill", "/IM", image, "/F"],
            capture_output=True,
            text=True,
            encoding="oem",
            errors="ignore",
        )
        if result.returncode == 0:
            killed.append(image)
    if killed:
        title = item.title if item else name
        return f"Закрыл {title}: " + ", ".join(killed)
    return f"«{item.title if item else name}» и так не запущен."


def copy_into_inbox(src: str, device_id: str) -> Path:
    path = _safe_path(src)
    if not path.is_file():
        raise ValueError(f"Это не файл: {path}")
    dest_dir = config.INBOX_DIR / device_id
    dest_dir.mkdir(parents=True, exist_ok=True)
    dest = dest_dir / path.name
    shutil.copy2(path, dest)
    return dest
