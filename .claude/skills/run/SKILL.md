---
description: Build and run the GameHelper WPF application from WSL2
---

# Running GameHelper

GameHelper — WPF-приложение (.NET 10, Windows). Запускается из WSL2 через Windows-версию dotnet.exe по WSL-interop.

## Prerequisites

WSL-interop должен быть активен (`/proc/sys/fs/binfmt_misc/WSLInterop` существует).
Если нет — установи `sudo apt install dotnet-sdk-10.0`, это регистрирует interop.

Проверка:
```bash
ls /proc/sys/fs/binfmt_misc/WSLInterop && echo "interop OK"
```

## Build

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build GameHelper.csproj -c Release
```

Успешный вывод заканчивается на:
```
GameHelper -> C:\Users\VVK\GameHelper\bin\Release\net10.0-windows10.0.17763.0\GameHelper.dll
Сборка успешно завершена.
```

## Run

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" run --project GameHelper.csproj --no-build -c Release
```

Приложение откроет Windows GUI-окно. Процесс завершится с exit code 0 при закрытии окна.

## Run tests

```bash
dotnet test GameHelper.Tests
```

Тесты запускаются через Linux dotnet SDK (уже в PATH).

## Notes

- `dotnet` (Linux) — для тестов и анализа кода
- `"/mnt/c/Program Files/dotnet/dotnet.exe"` (Windows) — для сборки и запуска WPF
- Не использовать `dotnet run` (Linux) для главного проекта — WPF требует Windows runtime
