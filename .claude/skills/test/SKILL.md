---
description: Run GameHelper test suite from WSL2
---

# Running tests

Тесты собраны под `net10.0-windows10.0.17763.0` и требуют Windows Desktop runtime — Linux dotnet SDK их не запустит. Используй Windows `dotnet.exe` через WSL-interop:

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" test GameHelper.Tests/GameHelper.Tests.csproj --configuration Release
```

Успешный вывод заканчивается строкой вида:
```
Пройден!   : не пройдено     0, пройдено  1452, пропущено     0, всего  1452
```

Если нужен подробный вывод по каждому тесту:
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" test GameHelper.Tests/GameHelper.Tests.csproj --configuration Release --logger "console;verbosity=normal"
```

## Notes

- Linux `dotnet test` не работает — нет `Microsoft.WindowsDesktop.App` в WSL2
- Флаг `-p:EnableWindowsTargeting=true` нужен только для `dotnet build` через Linux SDK, не для Windows dotnet.exe
