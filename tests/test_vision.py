import asyncio
import sys
import types

from PIL import Image

from makosh import config, vision
from tests.fakes import FakeLLM, make_agent


def _quiet_screen(monkeypatch):
    image = Image.new("RGB", (8, 8), "white")
    monkeypatch.setattr("makosh.agent.capture_image", lambda: image)
    monkeypatch.setattr("makosh.agent.local_ocr", lambda _image: "текст с экрана")
    monkeypatch.setattr(config, "BASE_URL", "https://openrouter.ai/api/v1")
    monkeypatch.setattr(config, "VISION_MODEL", "openai/gpt-4o-mini")
    return image


def test_vision_slot_limit_and_expiry(monkeypatch):
    monkeypatch.setattr(config, "VISION_MAX_PER_HOUR", 2)
    clock = {"now": 1_000.0}
    monkeypatch.setattr(vision.time, "time", lambda: clock["now"])
    assert vision.vision_calls_left() == 2
    assert vision.consume_vision_slot() is True
    assert vision.vision_calls_left() == 1
    assert vision.consume_vision_slot() is True
    assert vision.consume_vision_slot() is False
    assert vision.vision_calls_left() == 0
    clock["now"] += 3601
    assert vision.vision_calls_left() == 2
    assert vision.consume_vision_slot() is True


def test_ocr_mode_does_not_send_an_image(tmp_path, monkeypatch):
    _quiet_screen(monkeypatch)
    fake = FakeLLM([])
    agent, _memory = make_agent(tmp_path, fake)
    text = agent._look_screen("ocr")
    assert fake.calls == []
    assert "без облака" in text
    assert "текст с экрана" in text


def test_deepseek_backend_does_not_send_an_image(tmp_path, monkeypatch):
    _quiet_screen(monkeypatch)
    monkeypatch.setattr(config, "BASE_URL", "https://api.deepseek.com")
    fake = FakeLLM([])
    agent, _memory = make_agent(tmp_path, fake)
    text = agent._look_screen("vision")
    assert fake.calls == []
    assert "не умеет смотреть картинки" in text
    assert "текст с экрана" in text


def test_vision_over_the_hourly_limit_stays_on_ocr(tmp_path, monkeypatch):
    _quiet_screen(monkeypatch)
    monkeypatch.setattr(config, "VISION_MAX_PER_HOUR", 1)
    assert vision.consume_vision_slot() is True
    fake = FakeLLM([])
    agent, _memory = make_agent(tmp_path, fake)
    text = agent._look_screen("vision")
    assert fake.calls == []
    assert "Лимит облачного зрения исчерпан" in text
    assert "текст с экрана" in text


def test_missing_screenshot_becomes_a_tool_error(tmp_path, monkeypatch):
    def missing():
        raise FileNotFoundError("screenshot missing")

    monkeypatch.setattr("makosh.agent.capture_image", missing)
    agent, _memory = make_agent(tmp_path, FakeLLM([]))
    result = asyncio.run(agent._run_tool("look_screen", {"mode": "ocr"}))
    assert result.startswith("Ошибка инструмента look_screen")
    assert "screenshot missing" in result


def test_ocr_failure_becomes_a_tool_error(tmp_path, monkeypatch):
    monkeypatch.setattr(
        "makosh.agent.capture_image",
        lambda: Image.new("RGB", (4, 4)),
    )

    def broken(_image):
        raise RuntimeError("ocr failed")

    monkeypatch.setattr("makosh.agent.local_ocr", broken)
    agent, _memory = make_agent(tmp_path, FakeLLM([]))
    result = asyncio.run(agent._run_tool("look_screen", {"mode": "ocr"}))
    assert result.startswith("Ошибка инструмента look_screen")
    assert "ocr failed" in result


def test_rapidocr_is_constructed_on_every_local_ocr_call(monkeypatch):
    """Текущая стоимость, не цель.

    local_ocr создаёт RapidOCR() на каждый вызов. Кэш движка — следующий этап.
    M1 этот путь не меняет: тест только фиксирует поведение.
    """
    created: list[object] = []

    class FakeRapidOCR:
        def __init__(self):
            created.append(self)

        def __call__(self, _image):
            return ([(None, "строка")], None)

    module = types.ModuleType("rapidocr_onnxruntime")
    module.RapidOCR = FakeRapidOCR
    monkeypatch.setitem(sys.modules, "rapidocr_onnxruntime", module)
    vision.local_ocr(object())
    vision.local_ocr(object())
    assert len(created) == 2
