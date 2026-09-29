import logging

import pytest
from fastapi.testclient import TestClient

from makosh import config, hub
from makosh.hub import CHAT_FAILURE_REPLY
from tests.fakes import FakeLLM, text_response, unused_send
from makosh.agent import Agent
from makosh.memory import Memory


@pytest.fixture
def session(tmp_path):
    previous = hub.agent
    memory = Memory(tmp_path / "memory.sqlite")
    fake = FakeLLM([text_response("Ответ.")])
    hub.agent = Agent(memory, lambda: "нет устройств", unused_send, client=fake)
    try:
        with TestClient(hub.app) as client:
            yield client, fake, memory
    finally:
        hub.agent = previous


def _hello(ws, device_id="ws-1"):
    ws.send_json(
        {
            "token": config.TOKEN,
            "name": "phone",
            "kind": "android",
            "device_id": device_id,
        }
    )
    hello = ws.receive_json()
    assert hello["type"] == "hello"
    assert hello["device_id"] == device_id
    return hello


def test_health(session):
    client, _fake, _memory = session
    response = client.get("/api/health")
    assert response.status_code == 200
    assert response.json()["ok"] == "makosh"


def test_chat_requires_token_and_returns_fake_reply(session):
    client, fake, _memory = session
    denied = client.post("/api/chat", json={"text": "ping"})
    assert denied.status_code == 401
    accepted = client.post(
        "/api/chat",
        json={"text": "ping", "device_name": "web"},
        headers={"x-makosh-token": config.TOKEN},
    )
    assert accepted.status_code == 200
    assert accepted.json()["reply"] == "Ответ."
    assert len(fake.calls) == 1


def test_websocket_bad_token(session):
    client, _fake, _memory = session
    with client.websocket_connect("/ws") as ws:
        ws.send_json({"token": "wrong", "name": "phone", "kind": "android"})
        message = ws.receive_json()
        assert message["type"] == "error"
        assert "токен" in message["text"].lower()


def test_websocket_chat_roundtrip(session):
    client, _fake, _memory = session
    with client.websocket_connect("/ws") as ws:
        _hello(ws)
        ws.send_json({"type": "chat", "text": "привет", "speak": False})
        reply = ws.receive_json()
        assert reply["type"] == "reply"
        assert reply["text"] == "Ответ."
        assert reply["speak"] is False


def test_websocket_survives_llm_exception(session, caplog):
    client, fake, _memory = session
    caplog.set_level(logging.ERROR, logger="makosh.hub")
    fake.steps = [RuntimeError("llm down"), text_response("Снова на связи.")]
    with client.websocket_connect("/ws") as ws:
        _hello(ws, device_id="ws-resilient")
        ws.send_json({"type": "chat", "text": "сломайся", "speak": True})
        failed = ws.receive_json()
        assert failed["type"] == "reply"
        assert failed["text"] == CHAT_FAILURE_REPLY
        assert "ws-resilient" in hub.devices._devices

        ws.send_json({"type": "chat", "text": "ещё раз", "speak": True})
        recovered = ws.receive_json()
        assert recovered["type"] == "reply"
        assert recovered["text"] == "Снова на связи."

    health = client.get("/api/health")
    assert health.status_code == 200
    assert any(record.exc_info and str(record.exc_info[1]) == "llm down" for record in caplog.records)
    assert "ws-resilient" not in hub.devices._devices
