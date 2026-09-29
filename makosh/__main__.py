import threading

import uvicorn

from makosh import config
from makosh.certs import ensure_certs, lan_ips


def _run(port: int, ssl_certfile: str | None = None, ssl_keyfile: str | None = None) -> None:
    uvicorn.run(
        "makosh.hub:app",
        host=config.HOST,
        port=port,
        reload=False,
        ssl_certfile=ssl_certfile,
        ssl_keyfile=ssl_keyfile,
        log_level="info",
    )


if __name__ == "__main__":
    cert, key = ensure_certs()
    ips = ", ".join(lan_ips())
    print(f"HTTP  http://127.0.0.1:{config.PORT}")
    print(f"HTTPS https://127.0.0.1:{config.HTTPS_PORT}  (голос с телефона)")
    print(f"LAN IP: {ips}")
    thread = threading.Thread(
        target=_run,
        kwargs={
            "port": config.HTTPS_PORT,
            "ssl_certfile": str(cert),
            "ssl_keyfile": str(key),
        },
        daemon=True,
    )
    thread.start()
    _run(config.PORT)
