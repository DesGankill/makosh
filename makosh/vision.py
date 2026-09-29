from __future__ import annotations

import io
import time
from collections import deque
from pathlib import Path

from PIL import Image

from makosh import config

_vision_times: deque[float] = deque()


def _trim_vision_window() -> None:
    now = time.time()
    while _vision_times and now - _vision_times[0] > 3600:
        _vision_times.popleft()


def vision_calls_left() -> int:
    _trim_vision_window()
    return max(0, config.VISION_MAX_PER_HOUR - len(_vision_times))


def capture_image() -> Image.Image:
    import mss

    with mss.mss() as sct:
        monitor = sct.monitors[1]
        shot = sct.grab(monitor)
        return Image.frombytes("RGB", shot.size, shot.bgra, "raw", "BGRX")


def compact_jpeg(image: Image.Image, max_width: int | None = None) -> bytes:
    width = max_width or config.VISION_MAX_WIDTH
    img = image.convert("RGB")
    if img.width > width:
        ratio = width / img.width
        img = img.resize((width, int(img.height * ratio)), Image.Resampling.LANCZOS)
    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=45, optimize=True)
    return buf.getvalue()


def local_ocr(image: Image.Image) -> str:
    from rapidocr_onnxruntime import RapidOCR

    engine = RapidOCR()
    result, _ = engine(image)
    if not result:
        return ""
    lines = [item[1] for item in result if item and len(item) > 1]
    return "\n".join(lines).strip()


def save_preview(jpeg: bytes, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(jpeg)


def consume_vision_slot() -> bool:
    if vision_calls_left() <= 0:
        return False
    _vision_times.append(time.time())
    return True
