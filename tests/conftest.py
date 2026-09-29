import pytest


@pytest.fixture(autouse=True)
def _clear_vision_rate_window():
    from makosh import vision

    vision._vision_times.clear()
    yield
    vision._vision_times.clear()
