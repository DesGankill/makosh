import asyncio
import os
import subprocess
from pathlib import Path

import pytest

from makosh import tools
from tests.fakes import make_agent


@pytest.fixture
def rooted(tmp_path, monkeypatch):
    root = tmp_path / "root"
    root.mkdir()
    monkeypatch.setattr(tools, "SAFE_ROOTS", [root])
    return root


def test_file_inside_allowed_root(rooted):
    target = rooted / "notes" / "a.txt"
    target.parent.mkdir()
    target.write_text("hello", encoding="utf-8")
    assert tools._safe_path(str(target)) == target.resolve()
    listing = tools.list_files(str(target.parent))
    assert "a.txt" in listing


def test_absolute_path_inside_root(rooted):
    target = rooted / "b.txt"
    target.write_text("x", encoding="utf-8")
    assert tools._safe_path(str(target.resolve())) == target.resolve()


def test_missing_path_inside_root_is_reported(rooted):
    missing = rooted / "missing.txt"
    assert tools._safe_path(str(missing)) == missing.resolve()
    assert "Нет такого пути" in tools.list_files(str(missing))


def test_parent_segments_cannot_leave_root(rooted, tmp_path):
    secret = tmp_path / "secret.txt"
    secret.write_text("nope", encoding="utf-8")
    escaped = rooted / "nested" / ".." / ".." / "secret.txt"
    with pytest.raises(ValueError, match="вне разрешённых"):
        tools._safe_path(str(escaped))


def test_absolute_path_outside_root_is_denied(rooted):
    with pytest.raises(ValueError, match="вне разрешённых"):
        tools._safe_path(r"C:\Windows\System32\drivers\etc\hosts")


def test_sibling_prefix_is_not_inside_root(rooted):
    sibling = Path(str(rooted) + "_extra")
    sibling.mkdir()
    leaked = sibling / "x.txt"
    leaked.write_text("x", encoding="utf-8")
    with pytest.raises(ValueError, match="вне разрешённых"):
        tools._safe_path(str(leaked))


def test_home_shortcut_is_outside_a_narrow_root(rooted):
    with pytest.raises(ValueError, match="вне разрешённых"):
        tools._safe_path("~")


def test_agent_turns_outside_path_into_tool_error(rooted, tmp_path):
    agent, _memory = make_agent(tmp_path / "agent", client=None)
    result = asyncio.run(agent._run_tool("list_files", {"path": str(tmp_path / "secret.txt")}))
    assert "вне разрешённых" in result


def test_home_directory_is_currently_an_allowed_root():
    """Весь профиль пользователя сейчас разрешён. Сужение корней — отдельный поздний шаг."""
    assert any(root.resolve() == Path.home().resolve() for root in tools.SAFE_ROOTS)


def test_path_under_home_but_outside_documents_is_currently_allowed(tmp_path):
    """Демонстрация широкой границы: временный файл в профиле проходит без Desktop/Downloads/Documents."""
    resolved = tmp_path.resolve()
    home = Path.home().resolve()
    if home != resolved and home not in resolved.parents:
        pytest.skip("каталог теста лежит вне домашнего профиля")
    marker = tmp_path / "makosh-m1-boundary.txt"
    marker.write_text("x", encoding="utf-8")
    assert tools._safe_path(str(marker)) == marker.resolve()


def test_symlink_to_outside_file_is_denied(rooted, tmp_path):
    secret = tmp_path / "secret.txt"
    secret.write_text("hidden", encoding="utf-8")
    link = rooted / "alias.txt"
    try:
        os.symlink(secret, link)
    except OSError as exc:
        pytest.skip(f"символическая ссылка недоступна: {exc}")
    with pytest.raises(ValueError, match="вне разрешённых"):
        tools._safe_path(str(link))


def _junction(link: Path, target: Path) -> None:
    subprocess.run(
        ["cmd", "/c", "mklink", "/J", str(link), str(target)],
        check=True,
        capture_output=True,
    )


def test_directory_junction_to_outside_is_denied(rooted, tmp_path):
    """На этой Windows symlink без привилегии не создаётся. Junction — тот же resolve()."""
    outside = tmp_path / "outside"
    outside.mkdir()
    (outside / "secret.txt").write_text("hidden", encoding="utf-8")
    link = rooted / "jump"
    _junction(link, outside)
    with pytest.raises(ValueError, match="вне разрешённых"):
        tools._safe_path(str(link / "secret.txt"))


def test_directory_junction_inside_root_is_allowed(rooted):
    target = rooted / "real"
    target.mkdir()
    (target / "a.txt").write_text("ok", encoding="utf-8")
    link = rooted / "jump"
    _junction(link, target)
    assert tools._safe_path(str(link / "a.txt")) == (target / "a.txt").resolve()


def test_symlink_inside_root_is_allowed(rooted):
    target = rooted / "real.txt"
    target.write_text("ok", encoding="utf-8")
    link = rooted / "alias.txt"
    try:
        os.symlink(target, link)
    except OSError as exc:
        pytest.skip(f"символическая ссылка недоступна: {exc}")
    assert tools._safe_path(str(link)) == target.resolve()
