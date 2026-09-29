from __future__ import annotations

import logging
import uuid
from pathlib import Path

from fastapi import FastAPI, Header, HTTPException, UploadFile, WebSocket, WebSocketDisconnect
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

from makosh import config
from makosh.agent import Agent
from makosh.devices import Device, DeviceRegistry
from makosh.memory import Memory
from makosh.tools import copy_into_inbox

logger = logging.getLogger("makosh.hub")
CHAT_FAILURE_REPLY = "Не получилось обработать сообщение. Подробности в логе сервера."

STATIC = Path(__file__).resolve().parent / "static"
config.DATA_DIR.mkdir(parents=True, exist_ok=True)
config.INBOX_DIR.mkdir(parents=True, exist_ok=True)

app = FastAPI(title="Makosh")
memory = Memory(config.MEMORY_DB)
devices = DeviceRegistry()


async def send_file(path: str, device_hint: str) -> str:
    target = devices.find(device_hint)
    if not target:
        dest = copy_into_inbox(path, "unclaimed")
        return f"Устройство «{device_hint}» не в сети. Файл лежит в inbox: {dest}"
    dest = copy_into_inbox(path, target.device_id)
    url = f"/api/inbox/{target.device_id}/{dest.name}"
    await devices.send(
        target.device_id,
        {"type": "file", "name": dest.name, "url": url},
    )
    return f"Отправил {dest.name} на {target.name}. Ссылка: {url}"


agent = Agent(memory, devices.list_text, send_file)


def _check(token: str | None) -> None:
    if token != config.TOKEN:
        raise HTTPException(status_code=401, detail="Неверный токен")


class ChatBody(BaseModel):
    text: str
    speak: bool = True
    device_name: str = "web"


@app.get("/api/health")
def health() -> dict[str, str]:
    return {"ok": "makosh", "device": config.DEVICE_NAME}


@app.post("/api/chat")
async def chat(body: ChatBody, x_makosh_token: str | None = Header(default=None)) -> dict[str, str]:
    _check(x_makosh_token)
    reply = await agent.handle(body.text, body.device_name)
    return {"reply": reply}


def _bad_path_segment(value: str) -> bool:
    if value in {"", ".", ".."}:
        return True
    path = Path(value)
    return path.is_absolute() or path.name != value


def _inbox_destination(device_id: str, filename: str | None) -> Path:
    if _bad_path_segment(device_id):
        raise HTTPException(status_code=400, detail="Плохой путь")
    raw_name = filename or "file.bin"
    if _bad_path_segment(raw_name):
        raise HTTPException(status_code=400, detail="Плохой путь")
    root = config.INBOX_DIR.resolve()
    dest = (root / device_id / raw_name).resolve()
    if root not in dest.parents:
        raise HTTPException(status_code=400, detail="Плохой путь")
    dest.parent.mkdir(parents=True, exist_ok=True)
    return dest


@app.post("/api/upload/{device_id}")
async def upload(device_id: str, file: UploadFile, x_makosh_token: str | None = Header(default=None)) -> dict[str, str]:
    _check(x_makosh_token)
    dest = _inbox_destination(device_id, file.filename)
    dest.write_bytes(await file.read())
    return {"saved": str(dest)}


@app.get("/api/inbox/{device_id}/{name}")
def inbox_file(device_id: str, name: str, token: str = "") -> FileResponse:
    if token != config.TOKEN:
        raise HTTPException(status_code=401, detail="Неверный токен")
    path = (config.INBOX_DIR / device_id / name).resolve()
    if config.INBOX_DIR.resolve() not in path.parents:
        raise HTTPException(status_code=400, detail="Плохой путь")
    if not path.is_file():
        raise HTTPException(status_code=404, detail="Нет файла")
    return FileResponse(path, filename=name)


@app.websocket("/ws")
async def ws(socket: WebSocket) -> None:
    await socket.accept()
    device_id = ""
    try:
        hello = await socket.receive_json()
        if hello.get("token") != config.TOKEN:
            await socket.send_json({"type": "error", "text": "Неверный токен"})
            await socket.close()
            return
        device_id = str(hello.get("device_id") or uuid.uuid4())
        name = str(hello.get("name") or "device")
        kind = str(hello.get("kind") or "web")
        devices.register(Device(device_id=device_id, name=name, kind=kind, ws=socket))
        await socket.send_json({"type": "hello", "device_id": device_id, "name": config.DEVICE_NAME})
        while True:
            msg = await socket.receive_json()
            if msg.get("type") == "chat":
                try:
                    reply = await agent.handle(str(msg.get("text") or ""), name)
                except Exception:
                    logger.exception("Ошибка обработки сообщения")
                    reply = CHAT_FAILURE_REPLY
                await socket.send_json({"type": "reply", "text": reply, "speak": bool(msg.get("speak", True))})
    except WebSocketDisconnect:
        pass
    finally:
        if device_id:
            devices.drop(device_id)


if STATIC.is_dir():
    app.mount("/", StaticFiles(directory=STATIC, html=True), name="static")
