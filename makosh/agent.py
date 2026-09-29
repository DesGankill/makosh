from __future__ import annotations

import json
import re
from typing import Any, Callable, Awaitable

from makosh import config
from makosh.memory import Memory
from makosh.vision import capture_image, compact_jpeg, consume_vision_slot, local_ocr, vision_calls_left
from makosh import tools as pc_tools
from makosh.deepseek_chat import DeepSeekWebChat

SYSTEM = """Ты Makosh — голосовой помощник хозяина. Отвечай коротко, по-русски, без канцелярита.
Экран не смотри сам по себе: сначала локальный OCR. Облачное зрение — только если OCR пустой
и пользователь явно просит посмотреть. Не выдумывай память: читай через remember/recall.
Файлы на другое устройство отправляй инструментом send_file.
Действия на ПК — только через инструменты.
Не выдумывай net send, msg.exe, LanSend и учётные данные Windows. Удалённый ПК управляется только если там запущен Makosh."""

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
    {
        "type": "function",
        "function": {
            "name": "set_volume",
            "description": "Выставить системную громкость Windows от 0 до 100",
            "parameters": {
                "type": "object",
                "properties": {"percent": {"type": "integer"}},
                "required": ["percent"],
            },
        },
    },
]


def _explain_llm_error(exc: Exception) -> str:
    text = str(exc)
    low = text.lower()
    if "402" in text or "insufficient balance" in low:
        return (
            "DeepSeek отклонил запрос: на счёте нет денег (ошибка 402). "
            "Пополни баланс на https://platform.deepseek.com и напиши ещё раз."
        )
    if "401" in text or "invalid" in low and "key" in low:
        return "DeepSeek не принял ключ. Проверь OPENAI_API_KEY в .env."
    if "timeout" in low or "timed out" in low:
        return "DeepSeek не ответил вовремя. Попробуй ещё раз."
    return f"DeepSeek не ответил: {text[:400]}"


_TOOL_FENCE = re.compile(r"```(?:json)?\s*(\{.*?\})\s*```", re.S)


def _parse_tool(text: str) -> dict[str, Any] | None:
    blob = (text or "").strip()
    if not blob:
        return None
    fence = _TOOL_FENCE.search(blob)
    if fence:
        blob = fence.group(1).strip()
    start = blob.find("{")
    if start < 0:
        return None
    try:
        obj, _end = json.JSONDecoder().raw_decode(blob[start:])
    except json.JSONDecodeError:
        return None
    if isinstance(obj, dict) and obj.get("tool"):
        return obj
    return None


def _strip_pc_place(name: str) -> str:
    name = re.sub(
        r"^(на\s+)?(этом|втором|другом|первом)\s+(пк|компьютере|компе|pc)\s+",
        "",
        name,
    )
    name = re.sub(r"\s+на (?:этом|втором|другом|первом)?\s*(?:пк|компьютере|компе|pc)$", "", name)
    name = re.sub(r"^скрипт\s+", "", name)
    return name.strip(" .!")


def _wants_remote_pc(text: str) -> bool:
    low = text.lower()
    return bool(re.search(r"втор|друг(ой|ом|ого)|second|other", low)) and bool(
        re.search(r"пк|комп|computer|\bpc\b", low)
    )


def _wants_deepseek_edge(text: str) -> bool:
    low = text.lower().replace("ё", "е")
    if not re.search(r"открой|запусти|включи|open|run|скрипт", low):
        return False
    return pc_tools.is_deepseek_launcher(low)


def _close_from_text(text: str) -> str | None:
    low = text.lower().strip()
    match = re.search(
        r"(?:закрой|выключи|убей|останови)\s+(?:пожалуйста\s+)?(.+)$",
        low,
    )
    if not match:
        return None
    name = _strip_pc_place(match.group(1).strip(" .!"))
    if len(name) < 2:
        return None
    return name


def _is_ping(text: str) -> bool:
    low = text.lower().strip().strip("?.!")
    return low in {
        "ты тут",
        "ты здесь",
        "тут",
        "ау",
        "алло",
        "эй",
        "на связи",
        "живой",
        "ты живой",
        "слышно",
        "проверка",
        "ping",
    }


def _app_from_text(text: str) -> str | None:
    low = text.lower().strip()
    match = re.search(r"(?:открой|запусти|включи|open)\s+(?:пожалуйста\s+)?(.+)$", low)
    if not match:
        return None
    name = _strip_pc_place(match.group(1).strip(" .!"))
    if len(name) < 2:
        return None
    return name


def _volume_from_text(text: str) -> int | None:
    low = text.lower()
    match = re.search(r"(?:звук|громк|volume).{0,24}(?:до|на)\s*(\d{1,3})", low)
    if match:
        return max(0, min(100, int(match.group(1))))
    match = re.search(
        r"(?:уменьш|увелич|поставь|сделай).{0,24}(?:до|на)\s*(\d{1,3})",
        low,
    )
    if match and re.search(r"звук|громк|volume|пк|комп", low):
        return max(0, min(100, int(match.group(1))))
    match = re.search(r"(?:уменьш|увелич|поставь)\s+(?:на|до)\s*(\d{1,3})\s*%?\s*$", low)
    if match:
        return max(0, min(100, int(match.group(1))))
    return None


class Agent:
    def __init__(
        self,
        memory: Memory,
        list_devices: Callable[[], str],
        send_file: Callable[[str, str], Awaitable[str]],
        start_deepseek_remote: Callable[[], Awaitable[str]] | None = None,
    ) -> None:
        self.memory = memory
        self._list_devices = list_devices
        self._send_file = send_file
        self._start_deepseek_remote = start_deepseek_remote
        self._web = DeepSeekWebChat()
        self._client = None
        if config.LLM_BACKEND == "api" and config.API_KEY:
            from openai import OpenAI

            self._client = OpenAI(
                api_key=config.API_KEY,
                base_url=config.BASE_URL,
                timeout=60.0,
            )

    async def handle(self, text: str, speaker: str) -> str:
        self.memory.add_turn("user", f"[{speaker}] {text}")
        if _is_ping(text):
            reply = "Да, я здесь."
            self.memory.add_turn("assistant", reply)
            return reply
        percent = _volume_from_text(text)
        if percent is not None:
            try:
                reply = pc_tools.set_volume(percent)
            except Exception as exc:  # noqa: BLE001
                reply = f"Не удалось выставить громкость {percent}%: {exc}"
            self.memory.add_turn("assistant", reply)
            return reply
        close_name = _close_from_text(text)
        if close_name:
            reply = pc_tools.close_app(close_name)
            self.memory.add_turn("assistant", reply)
            return reply
        if _wants_deepseek_edge(text):
            if _wants_remote_pc(text) and self._start_deepseek_remote:
                reply = await self._start_deepseek_remote()
            else:
                reply = pc_tools.start_deepseek_edge()
            self.memory.add_turn("assistant", reply)
            return reply
        app_name = _app_from_text(text)
        if app_name:
            reply = pc_tools.open_app(app_name)
            self.memory.add_turn("assistant", reply)
            return reply
        try:
            if config.LLM_BACKEND != "api":
                reply = await self._web_loop(text)
            elif not self._client:
                reply = "LLM_BACKEND=api, но нет OPENAI_API_KEY."
            else:
                reply = await self._api_loop(text)
        except Exception as exc:  # noqa: BLE001
            reply = _explain_llm_error(exc)
        self.memory.add_turn("assistant", reply)
        return reply

    async def _web_loop(self, text: str) -> str:
        facts = self.memory.recall("")
        prompt = (
            f"Сообщение хозяина: {text}\n\nИзвестные факты:\n{facts}\n\n"
            "Если нужно действие на ПК — ответь только JSON. "
            'Громкость: {"tool":"set_volume","args":{"percent":10}}. '
            'Не используй press_hotkey для звука.'
        )
        for _ in range(5):
            raw = await self._web.ask(prompt)
            spec = _parse_tool(raw)
            if not spec:
                return raw.strip() or "Готово."
            args = spec.get("args") or {}
            if not isinstance(args, dict):
                args = {}
            result = await self._run_tool(str(spec["tool"]), args)
            prompt = (
                f"Результат инструмента {spec['tool']}: {result}\n"
                "Если нужно ещё действие — снова только JSON. Иначе короткий ответ человеку."
            )
        return "Слишком длинная цепочка действий, остановился."

    async def _api_loop(self, text: str) -> str:
        messages: list[dict[str, Any]] = [{"role": "system", "content": SYSTEM}]
        facts = self.memory.recall("")
        messages.append({"role": "system", "content": f"Известные факты:\n{facts}"})
        messages.extend(self.memory.recent_turns())
        return await self._loop(messages)

    async def _loop(self, messages: list[dict[str, Any]]) -> str:
        for _ in range(6):
            resp = self._client.chat.completions.create(
                model=config.MODEL,
                messages=messages,
                tools=TOOL_SCHEMAS,
                tool_choice="auto",
            )
            msg = resp.choices[0].message
            if not msg.tool_calls:
                return (msg.content or "").strip() or "Готово."
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
                args = json.loads(tc.function.arguments or "{}")
                result = await self._run_tool(tc.function.name, args)
                messages.append(
                    {"role": "tool", "tool_call_id": tc.id, "content": result}
                )

        return "Слишком длинная цепочка действий, остановился."

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
            if name in {"close_app", "kill_app", "quit_app"}:
                return pc_tools.close_app(args.get("name") or args.get("app") or "")
            if name == "open_path":
                return pc_tools.open_path(args["path"])
            if name == "type_text":
                return pc_tools.type_text(args["text"])
            if name == "press_hotkey":
                return pc_tools.press_hotkey(args.get("keys", ""))
            if name in {"set_volume", "volume", "change_volume"}:
                percent = args.get("percent", args.get("level", args.get("value")))
                return pc_tools.set_volume(percent)
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
