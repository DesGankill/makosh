from __future__ import annotations

import asyncio
import time
from typing import Any

from makosh import config

INPUT_JS = """
() => document.querySelector('#chat-input')
  || document.querySelector('textarea[placeholder*="DeepSeek" i]')
  || document.querySelector('textarea[placeholder*="сообщен" i]')
  || document.querySelector('textarea');
"""

ASSISTANT_JS = """
() => {
  const blocks = document.querySelectorAll('.ds-markdown');
  if (!blocks.length) return '';
  return blocks[blocks.length - 1].innerText.trim();
}
"""

COUNT_JS = """
() => document.querySelectorAll('.ds-markdown').length
"""

LOGIN_JS = """
() => {
  const t = document.body ? document.body.innerText : '';
  return /log in|sign in|войти|логин/i.test(t) && !document.querySelector('#chat-input');
}
"""


class DeepSeekWebChat:
    def __init__(self) -> None:
        self._lock = asyncio.Lock()
        self._pw: Any = None
        self._browser: Any = None
        self._page: Any = None
        self._primed = False

    async def ask(self, text: str) -> str:
        async with self._lock:
            page = await self._page_ready()
            before_count = await page.evaluate(COUNT_JS)
            before_text = await page.evaluate(ASSISTANT_JS)
            await self._type_and_send(page, text)
            return await self._wait_reply(page, before_count, before_text)

    async def _page_ready(self) -> Any:
        if self._page:
            try:
                if await self._page.evaluate(LOGIN_JS):
                    raise RuntimeError(
                        "В окне DeepSeek нужно войти в аккаунт. "
                        "Залогинься в Edge, который открыл скрипт start-deepseek-edge.ps1."
                    )
                if await self._page.query_selector("#chat-input, textarea"):
                    return self._page
            except RuntimeError:
                raise
            except Exception:
                self._page = None
        await self._connect()
        assert self._page is not None
        if await self._page.evaluate(LOGIN_JS):
            raise RuntimeError(
                "DeepSeek открылся, но нет входа. Войди в том окне Edge и повтори."
            )
        if not self._primed:
            before_count = await self._page.evaluate(COUNT_JS)
            before_text = await self._page.evaluate(ASSISTANT_JS)
            await self._type_and_send(
                self._page,
                "С этой минуты ты Makosh — голосовой помощник на моём ПК. "
                "Отвечай коротко по-русски. Если нужно действие на компьютере, "
                "ответь ТОЛЬКО JSON вида "
                '{"tool":"open_app","args":{"name":"блокнот"}} '
                "Инструменты: remember, recall, list_devices, list_files, send_file, "
                "look_screen, open_app, close_app, open_path, type_text, press_hotkey, set_volume. "
                'Закрыть программу: {"tool":"close_app","args":{"name":"steam"}}. '
                'Громкость только так: {"tool":"set_volume","args":{"percent":10}}. '
                "Иначе пиши обычный текст человеку, без JSON. Подтверди одним словом: готов.",
            )
            await self._wait_reply(self._page, before_count, before_text)
            self._primed = True
        return self._page

    async def _connect(self) -> None:
        from playwright.async_api import async_playwright

        if self._pw is None:
            self._pw = await async_playwright().start()
        last_err = ""
        try:
            self._browser = await self._pw.chromium.connect_over_cdp(config.DEEPSEEK_CDP)
            context = self._browser.contexts[0] if self._browser.contexts else None
            if context is None:
                raise RuntimeError("Edge открыт, но нет контекста")
            page = self._pick_page(context.pages)
            if page is None:
                page = await context.new_page()
                await page.goto(config.DEEPSEEK_CHAT_URL, wait_until="domcontentloaded")
            elif "deepseek.com" not in (page.url or ""):
                await page.goto(config.DEEPSEEK_CHAT_URL, wait_until="domcontentloaded")
            await page.wait_for_timeout(800)
            self._page = page
            return
        except Exception as exc:  # noqa: BLE001
            last_err = str(exc)
        profile = config.CHROME_PROFILE
        profile.mkdir(parents=True, exist_ok=True)
        try:
            context = await self._pw.chromium.launch_persistent_context(
                str(profile),
                channel=config.BROWSER_CHANNEL,
                headless=False,
                args=["--disable-blink-features=AutomationControlled"],
            )
            page = self._pick_page(context.pages) or await context.new_page()
            if "deepseek.com" not in (page.url or ""):
                await page.goto(config.DEEPSEEK_CHAT_URL, wait_until="domcontentloaded")
            self._page = page
            return
        except Exception as exc:  # noqa: BLE001
            raise RuntimeError(
                "Не удалось подключиться к чату DeepSeek. "
                "Запусти scripts\\start-deepseek-edge.ps1, войди в аккаунт, "
                f"потом повтори. CDP: {last_err}; Edge: {exc}"
            ) from exc

    def _pick_page(self, pages: list[Any]) -> Any | None:
        for page in pages:
            url = (page.url or "").lower()
            if "chat.deepseek.com" in url:
                return page
        for page in pages:
            if "deepseek.com" in (page.url or "").lower():
                return page
        return pages[0] if pages else None

    async def _type_and_send(self, page: Any, text: str) -> None:
        filled = await page.evaluate(
            """(value) => {
              const el = document.querySelector('#chat-input')
                || document.querySelector('textarea[placeholder*="DeepSeek" i]')
                || document.querySelector('textarea');
              if (!el) return false;
              const proto = window.HTMLTextAreaElement.prototype;
              const desc = Object.getOwnPropertyDescriptor(proto, 'value');
              if (desc && desc.set) desc.set.call(el, value);
              else el.value = value;
              el.dispatchEvent(new Event('input', { bubbles: true }));
              el.focus();
              return true;
            }""",
            text,
        )
        if not filled:
            locator = page.locator("#chat-input, textarea").first
            await locator.wait_for(timeout=15000)
            await locator.fill(text)
        await page.keyboard.press("Enter")

    async def _wait_reply(self, page: Any, before_count: int, before_text: str) -> str:
        deadline = time.monotonic() + 120
        last = ""
        stable_since = None
        started = False
        while time.monotonic() < deadline:
            await asyncio.sleep(0.45)
            count = await page.evaluate(COUNT_JS)
            text = await page.evaluate(ASSISTANT_JS)
            if count > before_count or (text and text != before_text):
                started = True
            if not started:
                continue
            if text != last:
                last = text
                stable_since = time.monotonic()
                continue
            if last and stable_since and time.monotonic() - stable_since >= 1.6:
                return last
        if last:
            return last
        raise RuntimeError("DeepSeek в браузере не дописал ответ за 2 минуты.")
