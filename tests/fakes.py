from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace
from typing import Any

from makosh.agent import Agent
from makosh.memory import Memory


class FakeLLM:
    def __init__(self, steps: list[Any]) -> None:
        self.steps = list(steps)
        self.calls: list[dict[str, Any]] = []
        self.chat = self
        self.completions = self

    def create(self, **kwargs: Any) -> Any:
        self.calls.append(kwargs)
        if not self.steps:
            raise AssertionError("fake LLM got an unexpected call")
        step = self.steps.pop(0)
        if isinstance(step, BaseException):
            raise step
        return step


def text_response(text: str) -> SimpleNamespace:
    message = SimpleNamespace(content=text, tool_calls=None)
    return SimpleNamespace(choices=[SimpleNamespace(message=message)])


def tool_response(name: str, arguments: str, call_id: str = "call-1") -> SimpleNamespace:
    call = SimpleNamespace(
        id=call_id,
        function=SimpleNamespace(name=name, arguments=arguments),
    )
    message = SimpleNamespace(content="", tool_calls=[call])
    return SimpleNamespace(choices=[SimpleNamespace(message=message)])


async def unused_send(path: str, device: str) -> str:
    return f"unused {path} {device}"


def make_agent(directory: Path, client: Any) -> tuple[Agent, Memory]:
    memory = Memory(directory / "memory.sqlite")
    agent = Agent(memory, lambda: "нет устройств", unused_send, client=client)
    return agent, memory
