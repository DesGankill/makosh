from __future__ import annotations

import hashlib
import re
import threading
import time
import uuid
from pathlib import Path

from makosh import config


def _clip(text: str) -> str:
    clean = re.sub(r"\s+", " ", text).strip()
    if len(clean) > 420:
        return clean[:420].rstrip() + "…"
    return clean


def play_file(path: Path) -> None:
    threading.Thread(target=_play_blocking, args=(path.resolve(),), daemon=True).start()


def _play_blocking(path: Path) -> None:
    try:
        _play_mci(path)
        return
    except Exception as exc:  # noqa: BLE001
        print(f"MCI play failed: {exc}")
    try:
        _play_wmp(path)
    except Exception as exc:  # noqa: BLE001
        print(f"WMP play failed: {exc}")


def _play_mci(path: Path) -> None:
    import ctypes

    winmm = ctypes.windll.winmm
    alias = "makosh"
    winmm.mciSendStringW(f"close {alias}", None, 0, None)
    opened = winmm.mciSendStringW(
        f'open "{path}" type mpegvideo alias {alias}',
        None,
        0,
        None,
    )
    if opened != 0:
        opened = winmm.mciSendStringW(f'open "{path}" alias {alias}', None, 0, None)
    if opened != 0:
        raise RuntimeError(f"mci open {opened}")
    played = winmm.mciSendStringW(f"play {alias} wait", None, 0, None)
    winmm.mciSendStringW(f"close {alias}", None, 0, None)
    if played != 0:
        raise RuntimeError(f"mci play {played}")


def _play_wmp(path: Path) -> None:
    from comtypes import CoInitialize, CoUninitialize
    from comtypes.client import CreateObject

    CoInitialize()
    try:
        player = CreateObject("WMPlayer.OCX")
        player.settings.volume = 100
        player.URL = str(path)
        player.controls.play()
        deadline = time.time() + 60
        while time.time() < deadline:
            state = int(player.playState)
            if state in {1, 8}:
                break
            time.sleep(0.15)
    finally:
        CoUninitialize()


def speak_sapi(text: str) -> None:
    spoken = _clip(text).replace("'", "''")
    if not spoken:
        return

    def _run() -> None:
        import subprocess

        cmd = (
            "Add-Type -AssemblyName System.Speech; "
            "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
            "$s.Volume = 100; "
            f"$s.Speak('{spoken}');"
        )
        subprocess.run(
            ["powershell", "-NoProfile", "-Command", cmd],
            check=False,
            capture_output=True,
        )

    threading.Thread(target=_run, daemon=True).start()


async def render_speech(text: str) -> Path | None:
    spoken = _clip(text)
    if not spoken:
        return None
    folder = config.DATA_DIR / "tts"
    folder.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha1(f"{config.TTS_VOICE}:{spoken}".encode("utf-8")).hexdigest()[:16]
    dest = folder / f"{digest}.mp3"
    if dest.is_file() and dest.stat().st_size > 0:
        return dest
    try:
        import edge_tts

        communicate = edge_tts.Communicate(spoken, config.TTS_VOICE)
        tmp = folder / f"{uuid.uuid4().hex}.mp3"
        await communicate.save(str(tmp))
        tmp.replace(dest)
        return dest
    except Exception as exc:  # noqa: BLE001
        print(f"TTS error: {exc}")
        return None


async def speak_reply(text: str) -> Path | None:
    path = await render_speech(text)
    if path:
        play_file(path)
        return path
    speak_sapi(text)
    return None
