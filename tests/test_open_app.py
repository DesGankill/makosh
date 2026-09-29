import pytest

from makosh import tools


@pytest.fixture
def launches(monkeypatch):
    popen_calls = []
    browser_calls = []

    def fake_popen(*args, **kwargs):
        popen_calls.append((args, kwargs))
        return object()

    def fake_browser(*args, **kwargs):
        browser_calls.append((args, kwargs))

    monkeypatch.setattr(tools.subprocess, "Popen", fake_popen)
    monkeypatch.setattr(tools.webbrowser, "open", fake_browser)
    return popen_calls, browser_calls


@pytest.mark.parametrize(
    ("name", "executable"),
    [
        ("notepad", "notepad"),
        ("Блокнот", "notepad"),
        ("explorer", "explorer"),
        ("калькулятор", "calc"),
    ],
)
def test_allowed_app_is_launched_by_name_only(launches, name, executable):
    popen_calls, browser_calls = launches
    result = tools.open_app(name)
    assert result == f"Запустил {executable}"
    assert popen_calls == [((executable,), {"shell": False})]
    assert browser_calls == []


def test_browser_name_opens_a_page_without_popen(launches):
    popen_calls, browser_calls = launches
    assert tools.open_app("браузер") == "Открыл браузер"
    assert popen_calls == []
    assert browser_calls[0][0] == ("https://google.com",)


def test_unknown_app_is_rejected(launches):
    popen_calls, browser_calls = launches
    result = tools.open_app("definitely-not-an-app")
    assert "не в белом списке" in result
    assert popen_calls == []
    assert browser_calls == []


def test_wrong_name_is_rejected(launches):
    popen_calls, _browser_calls = launches
    assert "не в белом списке" in tools.open_app("notpad")
    assert popen_calls == []


@pytest.mark.parametrize("name", ["", "   "])
def test_empty_name_is_rejected(launches, name):
    popen_calls, browser_calls = launches
    assert "не в белом списке" in tools.open_app(name)
    assert popen_calls == []
    assert browser_calls == []


@pytest.mark.parametrize(
    "name",
    [
        r"C:\Windows\System32\notepad.exe",
        r"..\notepad",
        "../notepad",
        r"notepad\calc",
    ],
)
def test_path_is_not_accepted_as_an_app_name(launches, name):
    popen_calls, browser_calls = launches
    result = tools.open_app(name)
    assert "не в белом списке" in result
    assert popen_calls == []
    assert browser_calls == []


def test_http_url_still_opens_in_the_browser(launches):
    popen_calls, browser_calls = launches
    assert tools.open_app("https://example.com") == "Открыл https://example.com"
    assert popen_calls == []
    assert browser_calls[0][0] == ("https://example.com",)


def test_cmd_is_currently_launchable(launches):
    """cmd есть в фактическом списке бинарников и не описан в README. В M1 это не закрывается."""
    popen_calls, browser_calls = launches
    assert tools.open_app("cmd") == "Запустил cmd"
    assert popen_calls == [(("cmd",), {"shell": False})]
    assert browser_calls == []
