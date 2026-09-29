import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parent.parent
load_dotenv(ROOT / ".env")

HOST = os.getenv("MAKOSH_HOST", "0.0.0.0")
PORT = int(os.getenv("MAKOSH_PORT", "8787"))
TOKEN = os.getenv("MAKOSH_TOKEN", "change-me-now")
DEVICE_NAME = os.getenv("MAKOSH_DEVICE_NAME", "PC")

API_KEY = os.getenv("OPENAI_API_KEY", "")
BASE_URL = os.getenv("OPENAI_BASE_URL", "https://openrouter.ai/api/v1")
MODEL = os.getenv("OPENAI_MODEL", "openai/gpt-4o-mini")
VISION_MODEL = os.getenv("VISION_MODEL", MODEL)

VISION_MAX_PER_HOUR = int(os.getenv("VISION_MAX_PER_HOUR", "6"))
VISION_MAX_WIDTH = int(os.getenv("VISION_MAX_WIDTH", "768"))

DATA_DIR = ROOT / "data"
INBOX_DIR = DATA_DIR / "inbox"
MEMORY_DB = DATA_DIR / "memory.sqlite"

ALLOWED_APPS = {
    "explorer": "explorer",
    "проводника": "explorer",
    "notepad": "notepad",
    "блокнот": "notepad",
    "calc": "calc",
    "калькулятор": "calc",
    "browser": "https://",
    "браузер": None,
}
