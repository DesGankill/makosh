import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parent.parent
load_dotenv(ROOT / ".env")

HOST = os.getenv("MAKOSH_HOST", "0.0.0.0")
PORT = int(os.getenv("MAKOSH_PORT", "8787"))
HTTPS_PORT = int(os.getenv("MAKOSH_HTTPS_PORT", "8443"))
TOKEN = os.getenv("MAKOSH_TOKEN", "change-me-now")
DEVICE_NAME = os.getenv("MAKOSH_DEVICE_NAME", "PC")

LLM_BACKEND = os.getenv("LLM_BACKEND", "deepseek_chat")
API_KEY = os.getenv("OPENAI_API_KEY", "")
BASE_URL = os.getenv("OPENAI_BASE_URL", "https://api.deepseek.com")
MODEL = os.getenv("OPENAI_MODEL", "deepseek-chat")
VISION_MODEL = os.getenv("VISION_MODEL", MODEL)
DEEPSEEK_CDP = os.getenv("DEEPSEEK_CDP", "http://127.0.0.1:9222")
DEEPSEEK_CHAT_URL = os.getenv("DEEPSEEK_CHAT_URL", "https://chat.deepseek.com")

VISION_MAX_PER_HOUR = int(os.getenv("VISION_MAX_PER_HOUR", "6"))
VISION_MAX_WIDTH = int(os.getenv("VISION_MAX_WIDTH", "768"))

DATA_DIR = ROOT / "data"
INBOX_DIR = DATA_DIR / "inbox"
MEMORY_DB = DATA_DIR / "memory.sqlite"
CHROME_PROFILE = Path(os.getenv("LOCALAPPDATA", str(DATA_DIR))) / "Makosh" / "edge-deepseek"
BROWSER_CHANNEL = os.getenv("MAKOSH_BROWSER", "msedge")

PEERS = [p.strip().rstrip("/") for p in os.getenv("MAKOSH_PEERS", "").split(",") if p.strip()]
TTS_VOICE = os.getenv("MAKOSH_TTS_VOICE", "ru-RU-SvetlanaNeural")
STT_MODEL = os.getenv("MAKOSH_STT_MODEL", "base")

ALLOWED_APPS = {
    "explorer": "explorer",
    "проводника": "explorer",
    "проводник": "explorer",
    "notepad": "notepad",
    "блокнот": "notepad",
    "calc": "calc",
    "калькулятор": "calc",
    "browser": "https://",
    "браузер": None,
    "steam": "steam",
    "стим": "steam",
    "discord": "discord",
    "дискорд": "discord",
    "telegram": "telegram",
    "телеграм": "telegram",
    "spotify": "spotify",
    "спотифай": "spotify",
    "edge": "msedge",
    "хром": "chrome",
    "chrome": "chrome",
    "blender": "blender",
    "блендер": "blender",
    "valheim": "valheim",
    "вальхейм": "valheim",
    "валхейм": "valheim",
    "вольхейн": "valheim",
    "вольхайн": "valheim",
    "валхейн": "valheim",
    "валехейм": "valheim",
}
