# Makosh

Хаб на Windows. Ответы берутся из **твоего чата** на [chat.deepseek.com](https://chat.deepseek.com) в Microsoft Edge, не из платного API.

## Запуск

1. Один раз поставить зависимости:

```powershell
cd "C:\Users\roma8\Desktop\7 sem\Makosh"
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

2. Открыть Edge с чатом DeepSeek (отдельный профиль, обычный Edge можно не закрывать):

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-deepseek-edge.ps1"
```

Войди в аккаунт в этом окне и **не закрывай его**. Можно открыть уже существующий диалог — Makosh будет писать туда.

3. Запустить хаб:

```powershell
python -m makosh
```

4. Открыть [http://127.0.0.1:8787](http://127.0.0.1:8787) и вставить `MAKOSH_TOKEN` из `.env`.

С телефона в той же Wi‑Fi: `http://IPv4-ПК:8787`.

## Если не отвечает

- Edge из скрипта должен быть запущен **до** хаба.
- Нужен вход в DeepSeek в этом окне.
- Не открывай второй раз тот же скрипт, пока первое окно живо.

Вернуть платный API: в `.env` поставь `LLM_BACKEND=api` и ключ `OPENAI_API_KEY`.
