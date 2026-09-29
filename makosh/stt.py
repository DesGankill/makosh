from __future__ import annotations

import os
from pathlib import Path

from makosh import config

_model = None


def load_model() -> None:
    global _model
    if _model is not None:
        return
    from faster_whisper import WhisperModel

    threads = max(4, os.cpu_count() or 4)
    _model = WhisperModel(
        config.STT_MODEL,
        device="cpu",
        compute_type="int8",
        cpu_threads=threads,
        num_workers=1,
    )


def transcribe_wav(path: Path) -> str:
    load_model()
    segments, _info = _model.transcribe(
        str(path),
        language="ru",
        vad_filter=True,
        beam_size=1,
        best_of=1,
        temperature=0,
        condition_on_previous_text=False,
        without_timestamps=True,
        initial_prompt=(
            "Русские команды компьютеру: открой, закрой, стим, блендер, громкость. "
            "Игры: Valheim, RimWorld, Hearts of Iron, Discord."
        ),
    )
    return " ".join(seg.text.strip() for seg in segments).strip()
