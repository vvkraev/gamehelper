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
"/mnt/c/Program Files/dotnet/dotnet.exe" test GameHelper.Tests/GameHelper.Tests.csproj --configuration Release
```

Тесты требуют Windows Desktop runtime — используй Windows dotnet.exe через interop, не Linux `dotnet`.

## Notes

- Linux `dotnet` — только для сборки с флагом `-p:EnableWindowsTargeting=true` (статический анализ, CI-check)
- `"/mnt/c/Program Files/dotnet/dotnet.exe"` (Windows) — для сборки, запуска и тестов WPF
- Не использовать `dotnet run` (Linux) для главного проекта — WPF требует Windows runtime
