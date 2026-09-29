from __future__ import annotations

import asyncio
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
from makosh.tools import copy_into_inbox, start_deepseek_edge
from makosh.voice import speak_reply
from makosh.stt import load_model, transcribe_wav
from makosh.certs import lan_ips

STATIC = Path(__file__).resolve().parent / "static"
config.DATA_DIR.mkdir(parents=True, exist_ok=True)
config.INBOX_DIR.mkdir(parents=True, exist_ok=True)

app = FastAPI(title="Makosh")


@app.on_event("startup")
async def _startup() -> None:
    asyncio.create_task(asyncio.to_thread(load_model))
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


def _lan_hint() -> str:
    ips = [ip for ip in lan_ips() if ip != "127.0.0.1"]
    host = ips[0] if ips else "IP-ПЕРВОГО-ПК"
    return f"http://{host}:{config.PORT}"


async def start_deepseek_remote() -> str:
    local = start_deepseek_edge()
    return (
        f"{local} "
        "Скрипт лежит только на первом ПК, поэтому Edge с DeepSeek открылся здесь. "
        "Второй компьютер я не запускаю: на нём нет Makosh. "
        f"С него можно лишь открыть чат в браузере: {_lan_hint()} "
        "и ввести тот же токен."
    )


agent = Agent(memory, devices.list_text, send_file, start_deepseek_remote)


def _check(token: str | None) -> None:
    if token != config.TOKEN:
        raise HTTPException(status_code=401, detail="Неверный токен")


class ChatBody(BaseModel):
    text: str
    speak: bool = True
    device_name: str = "web"


@app.get("/api/health")
def health() -> dict[str, str]:
    return {
        "ok": "makosh",
        "device": config.DEVICE_NAME,
        "llm": config.LLM_BACKEND,
        "https_port": str(config.HTTPS_PORT),
        "ips": ",".join(lan_ips()),
    }


@app.post("/api/stt")
async def stt_api(
    file: UploadFile,
    x_makosh_token: str | None = Header(default=None),
    token: str = "",
) -> dict[str, str]:
    _check(x_makosh_token or token)
    folder = config.DATA_DIR / "stt"
    folder.mkdir(parents=True, exist_ok=True)
    dest = folder / f"{uuid.uuid4().hex}.wav"
    dest.write_bytes(await file.read())
    try:
        text = await asyncio.to_thread(transcribe_wav, dest)
    except Exception as exc:  # noqa: BLE001
        raise HTTPException(status_code=500, detail=f"STT: {exc}") from exc
    return {"text": text}


async def _reply_payload(text: str, speaker: str, speak: bool) -> dict[str, str]:
    reply = await agent.handle(text, speaker)
    payload = {"reply": reply, "text": reply, "type": "reply", "speak": "1" if speak else "0"}
    if speak:
        asyncio.create_task(speak_reply(reply))
    return payload


class SpeakBody(BaseModel):
    text: str


@app.post("/api/speak")
async def speak_api(
    body: SpeakBody,
    x_makosh_token: str | None = Header(default=None),
    token: str = "",
) -> dict[str, str]:
    _check(x_makosh_token or token)
    audio = await render_speech(body.text)
    if not audio:
        return {"audio": ""}
    return {"audio": f"/api/tts/{audio.name}?token={config.TOKEN}"}


@app.post("/api/chat")
async def chat(
    body: ChatBody,
    x_makosh_token: str | None = Header(default=None),
    token: str = "",
) -> dict[str, str]:
    _check(x_makosh_token or token)
    return await _reply_payload(body.text, body.device_name, body.speak)


@app.post("/api/hands/start-deepseek")
def start_deepseek_hands(
    x_makosh_token: str | None = Header(default=None),
    token: str = "",
) -> dict[str, str]:
    _check(x_makosh_token or token)
    return {"ok": "1", "text": start_deepseek_edge()}


@app.get("/api/tts/{name}")
def tts_file(name: str, token: str = "") -> FileResponse:
    if token != config.TOKEN:
        raise HTTPException(status_code=401, detail="Неверный токен")
    path = (config.DATA_DIR / "tts" / name).resolve()
    if config.DATA_DIR.resolve() not in path.parents:
        raise HTTPException(status_code=400, detail="Плохой путь")
    if not path.is_file():
        raise HTTPException(status_code=404, detail="Нет файла")
    return FileResponse(path, media_type="audio/mpeg", filename=name)


@app.post("/api/upload/{device_id}")
async def upload(device_id: str, file: UploadFile, x_makosh_token: str | None = Header(default=None)) -> dict[str, str]:
    _check(x_makosh_token)
    folder = config.INBOX_DIR / device_id
    folder.mkdir(parents=True, exist_ok=True)
    dest = folder / (file.filename or "file.bin")
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
        host = ""
        if socket.client and socket.client.host:
            host = socket.client.host
        devices.register(Device(device_id=device_id, name=name, kind=kind, ws=socket, host=host))
        await socket.send_json({"type": "hello", "device_id": device_id, "name": config.DEVICE_NAME})
        while True:
            msg = await socket.receive_json()
            if msg.get("type") == "chat":
                try:
                    payload = await _reply_payload(
                        str(msg.get("text") or ""),
                        name,
                        True,
                    )
                except Exception as exc:  # noqa: BLE001
                    payload = {"type": "reply", "text": f"Сбой хаба: {exc}", "reply": f"Сбой хаба: {exc}"}
                await socket.send_json(payload)
    except WebSocketDisconnect:
        pass
    finally:
        if device_id:
            devices.drop(device_id)


if STATIC.is_dir():
    app.mount("/", StaticFiles(directory=STATIC, html=True), name="static")
