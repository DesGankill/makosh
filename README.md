# Makosh

Личный хаб на **Windows** (.NET 8 / ASP.NET Core): браузер на ПК и телефоне, SQLite-память, действия на ПК, передача файлов, HTTP/WebSocket, экран через локальный **Windows.Media.Ocr** и опциональное облачное зрение.

Основная реализация — C#. Python в `makosh/` сохранён как reference/backup и не нужен для обычного запуска.

## Development

```powershell
cd "C:\Users\roma8\Desktop\7 sem\Makosh"
copy .env.example .env
notepad .env
dotnet run --project dotnet/src/Makosh
```

Открой http://127.0.0.1:8787 (или `http://IP-КОМПЬЮТЕРА:8787` в той же Wi‑Fi сети).
Если страница с телефона не открывается — разреши **Makosh** в брандмауэре Windows.

## Tests

```powershell
dotnet test dotnet/Makosh.sln
```

Живой screenshot/OCR (не входит в обычный прогон):

```powershell
$env:MAKOSH_VISION_SMOKE='1'
dotnet test dotnet/Makosh.sln --filter FullyQualifiedName~WindowsVisionSmokeTests
```

## Release

```powershell
dotnet publish dotnet/src/Makosh/Makosh.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dotnet/publish/win-x64
```

Запуск опубликованного `Makosh.exe` (каталог `dotnet/publish/win-x64`). Скопируй `.env` рядом с exe или задай `MAKOSH_DATA_DIR` / `MAKOSH_ENV_FILE`. База `data/memory.sqlite` и `data/inbox/` живут рядом с `.env` или рядом с exe, а не в текущей рабочей папке проводника.

## Python (backup)

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python -m makosh
```

## .env

- `MAKOSH_TOKEN` — секрет для веб-интерфейса и API
- `MAKOSH_LLM_PROVIDER` — `openrouter` / `openai` / `xai` / `deepseek` / `local` / `browser` (Agent не меняется)
- `OPENAI_API_KEY` — ключ выбранного OpenAI-совместимого API (без ключа локальный OCR и Windows-инструменты остаются)
- `MAKOSH_LLM_FALLBACK` — необязательный второй провайдер после 429/5xx
- приложения: `data/apps.json` (имена вроде Blender), не правьте код ради списка
- при чистом OpenAI: `MAKOSH_LLM_PROVIDER=openai` или `OPENAI_BASE_URL=https://api.openai.com/v1`

Голос в Chrome / Edge: кнопка **Голос** — это распознавание речи в браузере. Ответы на ПК озвучивает локальный Windows TTS.

## Voice / TTS

Озвучка **локальная**. Текст никуда не отправляется, API key для TTS не нужен.

По умолчанию Makosh использует **Silero v5.5 RU** (`baya`, `kseniya`, `xenia`). SAPI остаётся fallback и отдельным движком.

- `MAKOSH_TTS_ENGINE` — `silero` (по умолчанию) или `sapi`
- `MAKOSH_TTS_VOICE` — для Silero: `kseniya` / `baya` / `xenia`; для SAPI: имя Windows-голоса
- `MAKOSH_TTS_DEVICE` — `cpu` (по умолчанию) или `cuda`, если установлен GPU-torch
- `MAKOSH_TTS_ENABLED` — включить/выключить серверный голос
- `MAKOSH_TTS_RATE` — скорость, от `-10` до `10`
- `MAKOSH_TTS_VOLUME` — громкость, от `0` до `100`
- `MAKOSH_TTS_PITCH` — тон, от `-10` до `10`

Список голосов: `GET /api/tts/voices` (поле `engine` у каждого голоса). В UI можно переключить Silero / SAPI.

**Первый запуск Silero:** нужен Python 3.12+ в PATH. Makosh один раз скачает модель `v5_5_ru.pt` (~139 МБ) в `data/tts/silero/` и поставит CPU-torch в `data/tts/silero/pydeps`. Пока worker не готов, ответы озвучивает SAPI. Если Silero уже работает и споткнулся на тексте, голос **не** переключается на SAPI. Markdown/латиница перед синтезом нормализуются.

```
MAKOSH_TTS_ENGINE=silero
MAKOSH_TTS_VOICE=kseniya
```

**Тон:** у Silero — через SSML модели (не через ускорение). У SAPI — SSML `prosody pitch`; OneCore часто игнорирует pitch.

Установка Windows-голосов — только через параметры Windows. Silero-голоса встроены в модель и не качаются по отдельности.

## Что умеет

- помнить факты между сессиями (`data/memory.sqlite`)
- открыть блокнот / проводник / калькулятор / браузер
- печатать и жать безопасные хоткеи
- список файлов в Desktop / Downloads / Documents
- отправить файл на подключённый телефон
- смотреть экран локальным OCR; облако — только по явной просьбе и не чаще 6 раз в час
- озвучивать ответы локально: Silero v5.5 RU или Windows SAPI
