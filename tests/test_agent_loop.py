import asyncio
import json
import logging

from makosh import tools
from tests.fakes import FakeLLM, make_agent, text_response, tool_response


def test_text_reply_without_tools(tmp_path):
    fake = FakeLLM([text_response("Привет.")])
    agent, memory = make_agent(tmp_path, fake)
    reply = asyncio.run(agent.handle("здравствуй", "web"))
    assert reply == "Привет."
    assert len(fake.calls) == 1
    assert fake.calls[0]["tools"]
    turns = memory.recent_turns()
    assert turns[0] == {"role": "user", "content": "[web] здравствуй"}
    assert turns[1] == {"role": "assistant", "content": "Привет."}


def test_tool_call_then_final_answer(tmp_path):
    fake = FakeLLM(
        [
            tool_response("remember", json.dumps({"key": "color", "value": "blue"})),
            text_response("Запомнил синий."),
        ]
    )
    agent, memory = make_agent(tmp_path, fake)
    reply = asyncio.run(agent.handle("запомни цвет", "web"))
    assert reply == "Запомнил синий."
    assert "color: blue" in memory.recall("color")
    assert len(fake.calls) == 2
    tool_messages = [item for item in fake.calls[1]["messages"] if item["role"] == "tool"]
    assert tool_messages
    assert "Запомнил: color = blue" in tool_messages[0]["content"]


def test_open_app_tool_uses_whitelist_without_a_real_process(tmp_path, monkeypatch):
    launched = []
    monkeypatch.setattr(
        tools.subprocess,
        "Popen",
        lambda *args, **kwargs: launched.append((args, kwargs)) or object(),
    )
    monkeypatch.setattr(tools.webbrowser, "open", lambda *args, **kwargs: launched.append(("web", args)))
    fake = FakeLLM(
        [
            tool_response("open_app", json.dumps({"name": "блокнот"})),
            text_response("Блокнот запущен."),
        ]
    )
    agent, _memory = make_agent(tmp_path, fake)
    reply = asyncio.run(agent.handle("открой блокнот", "pc"))
    assert reply == "Блокнот запущен."
    assert launched == [(("notepad",), {"shell": False})]
    tool_messages = [item for item in fake.calls[1]["messages"] if item["role"] == "tool"]
    assert "Запустил notepad" in tool_messages[0]["content"]


def test_invalid_tool_arguments_stay_in_the_loop(tmp_path, caplog):
    caplog.set_level(logging.ERROR, logger="makosh.agent")
    fake = FakeLLM(
        [
            tool_response("remember", "{not json"),
            text_response("Аргументы не разобрал."),
        ]
    )
    agent, memory = make_agent(tmp_path, fake)
    reply = asyncio.run(agent.handle("запомни", "web"))
    assert reply == "Аргументы не разобрал."
    assert memory.recall("color") == "В памяти пока пусто."
    tool_messages = [item for item in fake.calls[1]["messages"] if item["role"] == "tool"]
    assert "неверные аргументы" in tool_messages[0]["content"]
    assert any(record.exc_info for record in caplog.records)


def test_non_object_tool_arguments_do_not_crash(tmp_path):
    fake = FakeLLM(
        [
            tool_response("remember", "[]"),
            text_response("Нужен объект."),
        ]
    )
    agent, memory = make_agent(tmp_path, fake)
    reply = asyncio.run(agent.handle("запомни", "web"))
    assert reply == "Нужен объект."
    assert memory.recall("") == "В памяти пока пусто."


def test_missing_tool_fields_are_reported_to_the_model(tmp_path):
    fake = FakeLLM(
        [
            tool_response("remember", "{}"),
            text_response("Не хватает полей."),
        ]
    )
    agent, memory = make_agent(tmp_path, fake)
    reply = asyncio.run(agent.handle("запомни", "web"))
    assert reply == "Не хватает полей."
    tool_messages = [item for item in fake.calls[1]["messages"] if item["role"] == "tool"]
    assert tool_messages[0]["content"].startswith("Ошибка инструмента remember")
    assert memory.recall("") == "В памяти пока пусто."


def test_without_client_local_model_stays_off(tmp_path):
    agent, memory = make_agent(tmp_path, client=None)
    reply = asyncio.run(agent.handle("ping", "web"))
    assert "OPENAI_API_KEY" in reply
    assert memory.recent_turns()[-1]["content"] == reply
