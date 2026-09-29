import asyncio
import io

import pytest
from fastapi import HTTPException
from starlette.datastructures import UploadFile

from makosh import config, hub


def _file(name: str, payload: bytes = b"abc") -> UploadFile:
    return UploadFile(file=io.BytesIO(payload), filename=name)


def test_upload_and_download_roundtrip(session_inbox, tmp_path):
    client, inbox = session_inbox
    response = client.post(
        "/api/upload/phone",
        files={"file": ("note.txt", b"hello", "text/plain")},
        headers={"x-makosh-token": config.TOKEN},
    )
    assert response.status_code == 200
    saved = inbox / "phone" / "note.txt"
    assert saved.is_file()
    assert saved.read_bytes() == b"hello"
    assert inbox.resolve() in saved.resolve().parents

    downloaded = client.get("/api/inbox/phone/note.txt", params={"token": config.TOKEN})
    assert downloaded.status_code == 200
    assert downloaded.content == b"hello"

    denied = client.get("/api/inbox/phone/note.txt")
    assert denied.status_code == 401


def test_empty_file_and_unusual_name(session_inbox):
    client, inbox = session_inbox
    headers = {"x-makosh-token": config.TOKEN}
    empty = client.post(
        "/api/upload/phone",
        files={"file": ("empty.txt", b"", "text/plain")},
        headers=headers,
    )
    assert empty.status_code == 200
    assert (inbox / "phone" / "empty.txt").read_bytes() == b""

    unusual = "a b (1).txt"
    named = client.post(
        "/api/upload/phone",
        files={"file": (unusual, "чёрновик".encode("utf-8"), "text/plain")},
        headers=headers,
    )
    assert named.status_code == 200
    stored = inbox / "phone" / unusual
    assert stored.read_bytes() == "чёрновик".encode("utf-8")
    fetched = client.get(f"/api/inbox/phone/{unusual}", params={"token": config.TOKEN})
    assert fetched.status_code == 200
    assert fetched.content == stored.read_bytes()


def test_upload_requires_token(session_inbox):
    client, inbox = session_inbox
    response = client.post(
        "/api/upload/phone",
        files={"file": ("note.txt", b"x", "text/plain")},
    )
    assert response.status_code == 401
    assert not (inbox / "phone" / "note.txt").exists()


@pytest.mark.parametrize(
    "filename",
    [
        "../outside.txt",
        "..\\outside.txt",
        "../../pwn.txt",
        "foo/bar.txt",
        "foo\\bar.txt",
        "..",
        ".",
    ],
)
def test_upload_rejects_path_in_filename(session_inbox, tmp_path, filename):
    client, inbox = session_inbox
    outside = tmp_path / "pwn.txt"
    response = client.post(
        "/api/upload/phone",
        files={"file": (filename, b"pwned", "text/plain")},
        headers={"x-makosh-token": config.TOKEN},
    )
    assert response.status_code == 400
    assert not outside.exists()
    leaked = [path for path in tmp_path.rglob("*") if path.is_file() and inbox.resolve() not in path.resolve().parents]
    assert leaked == []


def test_upload_rejects_absolute_filename(session_inbox, tmp_path):
    _client, inbox = session_inbox
    outside = tmp_path / "absolute-target.txt"
    with pytest.raises(HTTPException) as caught:
        asyncio.run(hub.upload("phone", _file(str(outside), b"nope"), x_makosh_token=config.TOKEN))
    assert caught.value.status_code == 400
    assert not outside.exists()
    assert not any(inbox.rglob("*"))


def test_upload_rejects_device_id_traversal(session_inbox, tmp_path):
    outside = tmp_path / "escape.txt"
    with pytest.raises(HTTPException) as caught:
        asyncio.run(hub.upload("..", _file("escape.txt", b"nope"), x_makosh_token=config.TOKEN))
    assert caught.value.status_code == 400
    assert not outside.exists()


def test_download_rejects_parent_segments(session_inbox):
    _client, _inbox = session_inbox
    with pytest.raises(HTTPException) as parent_name:
        hub.inbox_file("phone", "..", token=config.TOKEN)
    assert parent_name.value.status_code == 400
    with pytest.raises(HTTPException) as parent_device:
        hub.inbox_file("..", "note.txt", token=config.TOKEN)
    assert parent_device.value.status_code == 400


@pytest.fixture
def session_inbox(tmp_path, monkeypatch):
    inbox = tmp_path / "inbox"
    monkeypatch.setattr(config, "INBOX_DIR", inbox)
    from fastapi.testclient import TestClient

    with TestClient(hub.app) as client:
        yield client, inbox
