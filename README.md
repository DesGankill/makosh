# Makosh

Личный хаб на Windows: голос и текст с ПК и Android (браузер), память, действия на ПК, передача файлов, экран через локальный OCR.

## Запуск

В PowerShell:

```powershell
cd "C:\Users\roma8\Desktop\7 sem\Makosh"
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
copy .env.example .env
notepad .env
python -m makosh
```

В `.env` обязательно:

- `MAKOSH_TOKEN` — свой секрет, его вводишь в веб-интерфейсе
- `OPENAI_API_KEY` — ключ OpenRouter / OpenAI / Groq
- при OpenAI смени `OPENAI_BASE_URL` на `https://api.openai.com/v1` и модель на `gpt-4o-mini`

Открой на этом ПК: http://127.0.0.1:8787

С телефона в той же Wi‑Fi сети: `http://IP-КОМПЬЮТЕРА:8787`  
IP смотри командой `ipconfig` (IPv4). Если страница не открывается — разреши Python в брандмауэре Windows.

Голос в Chrome / Edge: кнопка **Голос**, браузер спросит микрофон. Ответ читается вслух.

## Что уже умеет

- помнить факты между сессиями (`data/memory.sqlite`)
- открыть блокнот / проводник / калькулятор / браузер
- печатать и жать безопасные хоткеи
- список файлов в Desktop / Downloads / Documents
- отправить файл на подключённый телефон
- смотреть экран локальным OCR; облако — только по явной просьбе и не чаще 6 раз в час
