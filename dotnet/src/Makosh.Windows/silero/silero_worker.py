"""Local Silero v5.5 RU worker. Speaks over stdin/stdout; no HTTP, no cloud."""
from __future__ import annotations

import json
import os
import sys
import traceback
import warnings

warnings.filterwarnings("ignore")


def _configure_stdio() -> None:
    try:
        sys.stdin.reconfigure(encoding="utf-8", newline="\n")
        sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    except Exception:
        pass


def log(message: str) -> None:
    sys.stderr.write(message + "\n")
    sys.stderr.flush()


def read_line() -> str | None:
    line = sys.stdin.readline()
    if line == "":
        return None
    return line.strip("\r\n")


def write_json(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def write_pcm(pcm: bytes) -> None:
    sys.stdout.buffer.write(pcm)
    sys.stdout.buffer.flush()


def to_int16(audio) -> bytes:
    import numpy as np

    data = audio.detach().cpu().numpy() if hasattr(audio, "detach") else audio
    data = np.asarray(data, dtype=np.float32).reshape(-1)
    data = np.clip(data, -1.0, 1.0)
    return (data * 32767.0).astype("<i2").tobytes()


def wrap_ssml(text: str, rate: int, pitch: int) -> str:
    escaped = (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )
    rate_pct = 100 + max(-10, min(10, rate)) * 5
    pitch_pct = max(-10, min(10, pitch)) * 5
    pitch_attr = "default" if pitch_pct == 0 else f"{pitch_pct:+d}%"
    return (
        f'<speak><prosody rate="{rate_pct}%" pitch="{pitch_attr}">'
        f"{escaped}</prosody></speak>"
    )


def synth(model, speaker: str, text: str, sample_rate: int, rate: int, pitch: int):
    kwargs = dict(speaker=speaker, sample_rate=sample_rate, put_accent=True, put_yo=True)
    use_ssml = rate != 0 or pitch != 0
    if use_ssml:
        ssml = wrap_ssml(text, rate, pitch)
        try:
            return model.apply_tts(ssml_text=ssml, **kwargs)
        except TypeError:
            try:
                return model.apply_tts(ssml_text=ssml, speaker=speaker, sample_rate=sample_rate)
            except Exception:
                pass
        except Exception:
            pass
    return model.apply_tts(text=text, speaker=speaker, sample_rate=sample_rate)


def main() -> int:
    _configure_stdio()
    model_path = os.environ.get("MAKOSH_SILERO_MODEL", "")
    device_name = os.environ.get("MAKOSH_TTS_DEVICE", "cpu").strip().lower()
    if not model_path or not os.path.isfile(model_path):
        write_json({"ok": False, "error": "model-missing"})
        return 1

    import torch

    use_cuda = device_name == "cuda" and torch.cuda.is_available()
    device = torch.device("cuda" if use_cuda else "cpu")
    torch.set_num_threads(max(1, min(4, os.cpu_count() or 2)))
    model = torch.package.PackageImporter(model_path).load_pickle("tts_models", "model")
    model.to(device)
    speakers = list(getattr(model, "speakers", []) or [])
    write_json(
        {
            "ok": True,
            "device": "cuda" if use_cuda else "cpu",
            "speakers": speakers,
            "sample_rate": 48000,
        }
    )

    while True:
        raw = read_line()
        if raw is None:
            break
        if not raw:
            continue
        try:
            msg = json.loads(raw)
        except json.JSONDecodeError:
            write_json({"ok": False, "error": "bad-json"})
            continue
        op = msg.get("op")
        if op == "quit":
            write_json({"ok": True})
            break
        if op != "synth":
            write_json({"ok": False, "error": "unknown-op"})
            continue
        speaker = str(msg.get("speaker") or "kseniya")
        text = str(msg.get("text") or "")
        if not text.strip():
            write_json({"ok": False, "error": "empty"})
            continue
        if speakers and speaker not in speakers:
            speaker = speakers[0]
        try:
            audio = synth(
                model,
                speaker,
                text,
                int(msg.get("sample_rate") or 48000),
                int(msg.get("rate") or 0),
                int(msg.get("pitch") or 0),
            )
            pcm = to_int16(audio)
            write_json({"ok": True, "sr": int(msg.get("sample_rate") or 48000), "bytes": len(pcm)})
            write_pcm(pcm)
        except Exception as ex:
            log("silero synth error: " + type(ex).__name__ + ": " + str(ex))
            log(traceback.format_exc().splitlines()[-1])
            write_json({"ok": False, "error": type(ex).__name__})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
