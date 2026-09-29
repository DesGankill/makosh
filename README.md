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
- `OPENAI_API_KEY` — ключ OpenRouter / OpenAI / Groq (без ключа локальный OCR и Windows-инструменты остаются, облачная модель выключена)
- при чистом OpenAI: `OPENAI_BASE_URL=https://api.openai.com/v1` и модель `gpt-4o-mini`

Голос в Chrome / Edge: кнопка **Голос**. Ответ читается вслух в браузере.

## Что умеет

- помнить факты между сессиями (`data/memory.sqlite`)
- открыть блокнот / проводник / калькулятор / браузер
- печатать и жать безопасные хоткеи
- список файлов в Desktop / Downloads / Documents
- отправить файл на подключённый телефон
- смотреть экран локальным OCR; облако — только по явной просьбе и не чаще 6 раз в час
