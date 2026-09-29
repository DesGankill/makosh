from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Any

from fastapi import WebSocket


@dataclass
class Device:
    device_id: str
    name: str
    kind: str
    ws: WebSocket
    host: str = ""
    last_seen: float = field(default_factory=time.time)


class DeviceRegistry:
    def __init__(self) -> None:
        self._devices: dict[str, Device] = {}

    def register(self, device: Device) -> None:
        self._devices[device.device_id] = device

    def drop(self, device_id: str) -> None:
        self._devices.pop(device_id, None)

    def get(self, device_id: str) -> Device | None:
        return self._devices.get(device_id)

    def find(self, hint: str) -> Device | None:
        h = hint.strip().lower()
        for d in self._devices.values():
            if d.device_id.lower() == h or d.name.lower() == h or d.kind.lower() == h:
                return d
            if h in d.name.lower() or h in d.kind.lower():
                return d
        aliases = {
            "телефон": "android",
            "андроид": "android",
            "phone": "android",
            "пк": "pc",
            "комп": "pc",
            "компьютер": "pc",
        }
        kind = aliases.get(h)
        if kind:
            for d in self._devices.values():
                if d.kind == kind:
                    return d
        return None

    def list_text(self) -> str:
        if not self._devices:
            return "Сейчас никто не подключён, кроме этого хаба."
        lines = []
        for d in self._devices.values():
            lines.append(f"- {d.name} ({d.kind}, id={d.device_id})")
        return "\n".join(lines)

    def remote_base_urls(self, port: int) -> list[str]:
        urls: list[str] = []
        seen: set[str] = set()
        skip = {"127.0.0.1", "::1", "localhost", ""}
        for d in self._devices.values():
            host = (d.host or "").strip("[]")
            if host in skip:
                continue
            url = f"http://{host}:{port}"
            if url not in seen:
                seen.add(url)
                urls.append(url)
        return urls

    async def send(self, device_id: str, payload: dict[str, Any]) -> bool:
        device = self._devices.get(device_id)
        if not device:
            return False
        await device.ws.send_json(payload)
        return True
