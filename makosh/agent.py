from __future__ import annotations

import json
import logging
from typing import Any, Callable, Awaitable

from openai import OpenAI

from makosh import config
from makosh.memory import Memory
from makosh.vision import capture_image, compact_jpeg, consume_vision_slot, local_ocr, vision_calls_left
from makosh import tools as pc_tools

logger = logging.getLogger("makosh.agent")
_MISSING = object()

SYSTEM = """Ты Makosh — голосовой помощник хозяина. Отвечай коротко, по-русски, без канцелярита.
Экран не смотри сам по себе: сначала локальный OCR. Облачное зрение — только если OCR пустой
и пользователь явно просит посмотреть. Не выдумывай память: читай через remember/recall.
Файлы на другое устройство отправляй инструментом send_file.
Действия на ПК — только через инструменты."""

TOOL_SCHEMAS = [
    {
        "type": "function",
        "function": {
            "name": "remember",
            "description": "Сохранить факт в долгую память",
            "parameters": {
                "type": "object",
                "properties": {
                    "key": {"type": "string"},
                    "value": {"type": "string"},
                },
                "required": ["key", "value"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "recall",
            "description": "Найти факты в памяти",
            "parameters": {
                "type": "object",
                "properties": {"query": {"type": "string"}},
                "required": ["query"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "list_devices",
            "description": "Список подключённых устройств",
            "parameters": {"type": "object", "properties": {}},
        },
    },
    {
        "type": "function",
        "function": {
            "name": "list_files",
            "description": "Список файлов в папке (Рабочий стол, Загрузки, Документы)",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string"}},
                "required": ["path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "send_file",
            "description": "Скопировать файл в inbox устройства и отправить ссылку",
            "parameters": {
                "type": "object",
                "properties": {
                    "path": {"type": "string"},
                    "device": {"type": "string", "description": "телефон, android, имя устройства"},
                },
                "required": ["path", "device"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "look_screen",
            "description": "Посмотреть экран ПК. mode=ocr (бесплатно) или vision (облако, лимит)",
            "parameters": {
                "type": "object",
                "properties": {
                    "mode": {"type": "string", "enum": ["ocr", "vision"]},
                },
                "required": ["mode"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "open_app",
            "description": "Открыть приложение из белого списка",
            "parameters": {
                "type": "object",
                "properties": {"name": {"type": "string"}},
                "required": ["name"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "open_path",
            "description": "Открыть файл или папку",
            "parameters": {
                "type": "object",
                "properties": {"path": {"type": "string"}},
                "required": ["path"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "type_text",
            "description": "Напечатать текст в активном окне ПК",
            "parameters": {
                "type": "object",
                "properties": {"text": {"type": "string"}},
                "required": ["text"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "press_hotkey",
            "description": "Нажать комбинацию, например ctrl c",
            "parameters": {
                "type": "object",
                "properties": {"keys": {"type": "string"}},
                "required": ["keys"],
            },
        },
    },
]


class Agent:
    def __init__(
        self,
        memory: Memory,
        list_devices: Callable[[], str],
        send_file: Callable[[str, str], Awaitable[str]],
        client: Any = _MISSING,
    ) -> None:
        self.memory = memory
        self._list_devices = list_devices
        self._send_file = send_file
        self._client: Any = None
        if client is not _MISSING:
            self._client = client
        elif config.API_KEY:
            self._client = OpenAI(api_key=config.API_KEY, base_url=config.BASE_URL)

    async def handle(self, text: str, speaker: str) -> str:
        self.memory.add_turn("user", f"[{speaker}] {text}")
        if not self._client:
            reply = "Нет OPENAI_API_KEY в .env — облачная модель выключена. Могу только локальные команды позже."
            self.memory.add_turn("assistant", reply)
            return reply

        messages: list[dict[str, Any]] = [{"role": "system", "content": SYSTEM}]
        facts = self.memory.recall("")
        messages.append({"role": "system", "content": f"Известные факты:\n{facts}"})
        messages.extend(self.memory.recent_turns())

        for _ in range(6):
            resp = self._client.chat.completions.create(
                model=config.MODEL,
                messages=messages,
                tools=TOOL_SCHEMAS,
                tool_choice="auto",
            )
            msg = resp.choices[0].message
            if not msg.tool_calls:
                reply = (msg.content or "").strip() or "Готово."
                self.memory.add_turn("assistant", reply)
                return reply
            messages.append(
                {
                    "role": "assistant",
                    "content": msg.content or "",
                    "tool_calls": [
                        {
                            "id": tc.id,
                            "type": "function",
                            "function": {
                                "name": tc.function.name,
                                "arguments": tc.function.arguments,
                            },
                        }
                        for tc in msg.tool_calls
                    ],
                }
            )
            for tc in msg.tool_calls:
                result = await self._tool_result(tc.function.name, tc.function.arguments)
                messages.append(
                    {"role": "tool", "tool_call_id": tc.id, "content": result}
                )

        reply = "Слишком длинная цепочка действий, остановился."
        self.memory.add_turn("assistant", reply)
        return reply

    async def _tool_result(self, name: str, raw_arguments: str | None) -> str:
        try:
            args = json.loads(raw_arguments or "{}")
        except json.JSONDecodeError:
            logger.exception("Некорректный JSON аргументов инструмента %s", name)
            return f"Ошибка инструмента {name}: неверные аргументы"
        if not isinstance(args, dict):
            logger.error("Аргументы инструмента %s не объект", name)
            return f"Ошибка инструмента {name}: неверные аргументы"
        return await self._run_tool(name, args)

    async def _run_tool(self, name: str, args: dict[str, Any]) -> str:
        try:
            if name == "remember":
                return self.memory.remember(args["key"], args["value"])
            if name == "recall":
                return self.memory.recall(args.get("query", ""))
            if name == "list_devices":
                return self._list_devices()
            if name == "list_files":
                return pc_tools.list_files(args.get("path", ""))
            if name == "send_file":
                return await self._send_file(args["path"], args["device"])
            if name == "look_screen":
                return self._look_screen(args.get("mode", "ocr"))
            if name == "open_app":
                return pc_tools.open_app(args["name"])
            if name == "open_path":
                return pc_tools.open_path(args["path"])
            if name == "type_text":
                return pc_tools.type_text(args["text"])
            if name == "press_hotkey":
                return pc_tools.press_hotkey(args["keys"])
            return f"Неизвестный инструмент: {name}"
        except Exception as exc:  # noqa: BLE001
            return f"Ошибка инструмента {name}: {exc}"

    def _look_screen(self, mode: str) -> str:
        image = capture_image()
        text = local_ocr(image)
        if mode != "vision":
            if text:
                return f"OCR экрана (без облака):\n{text[:4000]}"
            return "OCR ничего не разобрал. DeepSeek не принимает картинки — облачное зрение недоступно, только текст с экрана."
        if "deepseek" in (config.BASE_URL or "").lower() or "deepseek" in (config.VISION_MODEL or "").lower():
            leftover = text or "(пусто)"
            return (
                "DeepSeek chat не умеет смотреть картинки. "
                f"Локальный OCR:\n{leftover[:4000]}"
            )
        if not consume_vision_slot():
            leftover = text or "(пусто)"
            return (
                f"Лимит облачного зрения исчерпан ({config.VISION_MAX_PER_HOUR}/час). "
                f"Остался OCR:\n{leftover[:3000]}"
            )
        jpeg = compact_jpeg(image)
        if not self._client:
            return "Нет ключа API для зрения."
        import base64

        b64 = base64.b64encode(jpeg).decode("ascii")
        resp = self._client.chat.completions.create(
            model=config.VISION_MODEL,
            messages=[
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "text",
                            "text": "Кратко опиши, что на экране. OCR рядом, сверься с ним:\n"
                            + (text[:1500] or "OCR пуст"),
                        },
                        {
                            "type": "image_url",
                            "image_url": {"url": f"data:image/jpeg;base64,{b64}"},
                        },
                    ],
                }
            ],
        )
        left = vision_calls_left()
        body = resp.choices[0].message.content or ""
        return f"{body}\n\n(облачных кадров осталось в этом часе: {left})"
