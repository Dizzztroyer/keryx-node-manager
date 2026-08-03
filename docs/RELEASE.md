# Процесс релиза

1. Обновить `CHANGELOG.md` и версию (`<Version>` в `KeryxNodeManager.App.csproj`, а также
   `installer\KeryxNodeManager.iss`).
2. `dotnet test tests\KeryxNodeManager.Core.Tests\...` — все тесты должны быть зелёными.
3. `dotnet publish` self-contained win-x64 (см. `docs/BUILD.md`).
4. Собрать portable ZIP (`scripts\package-portable.ps1`).
5. Собрать установщик (`installer\KeryxNodeManager.iss` через ISCC.exe).
6. Сгенерировать `artifacts\checksums.txt` (`scripts\build-release.ps1` делает шаги 3-6 одной
   командой).
7. Вручную: запустить установленную версию, пройти мастер первого запуска в `--mock` режиме и на
   реальном железе, проверить трей, проверить автозапуск, проверить сворачивание/восстановление
   окна.
8. Подписать `.exe`/установщик сертификатом code-signing, если он есть — без подписи Windows
   SmartScreen будет показывать предупреждение "неизвестный издатель" (см. `docs/SECURITY.md`).
9. Создать GitHub Release (или иное распространение) с обоими файлами из `artifacts\` и
   `checksums.txt`.

## Обновление самого Keryx Node Manager / ноды / майнера

Брифом (§19) описан полноценный self-update flow с changelog, checksum-проверкой, backup конфига,
rollback при неудаче. **В этой сессии он не реализован** — см. `PROJECT_STATUS.md`. Пока что
обновление ноды/майнера — это вручную скачать новый релиз с
`github.com/Keryx-Labs/keryx-node`/`keryx-miner` и заменить исполняемый файл в папке приложения;
обновление самого Keryx Node Manager — вручную скачать новую версию установщика/portable ZIP.
