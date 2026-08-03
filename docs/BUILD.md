# Сборка Keryx Node Manager

Эта сборка была написана и протестирована (Core-библиотека и WPF-приложение — компиляция;
55 unit-тестов — реальный прогон) в Linux-песочнице с .NET 8 SDK, но `KeryxNodeManager.App`
targeting `net8.0-windows` — сам исполняемый `.exe` можно собрать и запустить только на Windows.
Ниже — точные команды для сборки на вашей машине.

## Требования

- Windows 10/11 x64.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (не только Runtime — нужен SDK
  для сборки).
- Visual Studio 2022 (17.9+) с workload ".NET desktop development" — опционально, удобнее для
  разработки, но не обязательно: всё собирается через `dotnet` CLI.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — только для сборки установщика (`.exe`).

## Быстрая сборка и тесты

```powershell
cd KeryxNodeManager
dotnet restore
dotnet build -c Release
dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release
```

`KeryxNodeManager.Core.Tests` не требует Windows — при желании можно гонять эти тесты и в CI на
Linux-раннере, только сам `KeryxNodeManager.App` (WPF) требует Windows-таргет.

## Self-contained публикация (для portable ZIP и установщика)

```powershell
dotnet publish src\KeryxNodeManager.App\KeryxNodeManager.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false `
  -o artifacts\publish\win-x64
```

`--self-contained true` включает .NET Runtime в поставку — пользователю не нужно отдельно
устанавливать .NET. `PublishSingleFile=false` оставлен явно: WPF plus
`Hardcodet.NotifyIcon.Wpf`-иконки лучше распаковывать как обычный набор файлов на первом этапе,
пока не протестирована сборка в single-file режиме на реальной машине.

## Portable ZIP

```powershell
powershell -File scripts\package-portable.ps1
```

Собирает `artifacts\publish\win-x64` в
`artifacts\KeryxNodeManager-Portable-<version>.zip`, включая `README.md` и пример конфигурации.

## Установщик (.exe)

```powershell
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\KeryxNodeManager.iss
```

Результат: `artifacts\KeryxNodeManager-Setup-<version>.exe`. `installer\KeryxNodeManager.iss`
ожидает, что `dotnet publish` (см. выше) уже выполнен и `artifacts\publish\win-x64` существует.

## Контрольные суммы

```powershell
powershell -File scripts\build-release.ps1
```

Прогоняет publish → portable ZIP → installer → генерирует `artifacts\checksums.txt` (SHA256 для
обоих файлов).

## Что реально проверено в этой сессии, а что нет

Проверено (реальная компиляция/прогон в CI-подобной среде, не просто "код выглядит правильно"):
`KeryxNodeManager.Core` собирается под `net8.0` (кроссплатформенно); `KeryxNodeManager.Core.Tests`
— 55/55 тестов проходят; `KeryxNodeManager.App` (WPF, `net8.0-windows`) успешно компилируется
через `dotnet build -p:EnableWindowsTargeting=true` — это подтверждает, что XAML, code-behind,
DI-конфигурация и все ссылки между проектами корректны на уровне компилятора.

Не проверено в этой сессии (нет доступа к Windows/GPU): реальный запуск `.exe`, поведение трея,
UAC-подсказки, установка через Inno Setup, поведение `NativeWindowsRuntimeBackend` против
настоящих `keryxd.exe`/`keryx-miner.exe`, DPI-масштабирование на реальном экране. См.
`PROJECT_STATUS.md` → "Build status"/"Test status" за точным списком.
