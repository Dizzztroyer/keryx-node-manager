# Keryx Node Manager

*In anderen Sprachen lesen: [English](README.md) · [Русский](README.ru.md) · [Español](README.es.md) · [Français](README.fr.md)*

Eine Windows-App zur Verwaltung eines Keryx-Knotens (Node) und eines GPU-Miners aus einem einzigen
Fenster heraus — ohne manuelle Arbeit in PowerShell/WSL/Docker. Ein Community-Tool, kein
offizielles Produkt von Keryx Labs.

## Herunterladen und installieren

Gehe zur Seite **[Releases](https://github.com/Dizzztroyer/keryx-node-manager/releases/latest)**
und lade eine der beiden Dateien herunter:

- **`KeryxNodeManager-Setup-X.Y.Z.exe`** — normaler Installer. Starten, dem Assistenten folgen,
  fertig. Eine Verknüpfung wird auf dem Desktop und im Startmenü angelegt. Keine
  Administratorrechte erforderlich.
- **`KeryxNodeManager-Portable-X.Y.Z.zip`** — portable Version ohne Installation. Irgendwo
  entpacken und `KeryxNodeManager.exe` starten.

Beim ersten Start führt dich ein Einrichtungsassistent durch eine Systemprüfung, die Eingabe
deiner Mining-Adresse und das Erstellen/Auswählen eines Profils — danach öffnet sich direkt das
Dashboard, wobei Node und Miner bereits gestartet werden.

**Voraussetzungen:** Windows 10/11 x64, eine NVIDIA-GPU (für automatische Erkennung und
Übertaktung). Die Node-Binärdatei (`keryxd.exe`) und die Miner-Binärdatei (`keryx-miner.exe`) sind
nicht im Installer selbst enthalten, aber die App lädt und installiert sie automatisch, sobald sie
zum ersten Mal benötigt werden — es gibt keine separate Updates-Seite zu besuchen und keinen Pfad
manuell einzugeben.

## Funktionen

- Das Dashboard zeigt Node-, Miner- und GPU-Status gemeinsam an, mit einer einzigen
  Alle-starten/Alle-stoppen-Steuerung für Node und Miner zusammen, plus ein Tray-Symbol mit
  Live-Status.
- Automatische GPU-Erkennung, automatische Zuweisung der Mining-Stufe nach VRAM oder manuelle
  Auswahl pro Karte.
- GPU-Übertaktung (Kern/Speicher) und Lüftersteuerung — abgesichert durch einen
  Bestätigungsdialog.
- Ein-Klick-Download offizieller Modelle (HTTP + Torrent-Spiegel), mit einer manuellen Option
  (fortsetzbar, mit Integritätsprüfung) als Rückfalllösung.
- Verzeichnis öffentlicher Nodes sowie automatische Peer-Erkennung über den eigenen Node;
  Umschalten auf einen Backup-Node während der eigene synchronisiert, mit automatischer
  Rückschaltung, sobald dieser aufgeholt hat.
- Ein-Klick-Download und -Entpacken des Data-Dir (Direktlink oder Torrent).
- Protokolle mit automatischer Maskierung von Geheimnissen, Diagnose-Export.
- Überhitzungsschutz, Option für automatischen Start mit Windows.
- Mehrere Profile, Oberfläche in 6 Sprachen verfügbar (ru/en/es/it/fr/uk).
- Integrierter Update-Checker für Node und Miner.

## Sicherheit

Die App fragt niemals nach Seed-Phrasen oder privaten Schlüsseln und speichert sie auch nicht.
Jede RPC-Adresse, auf der die App antworten kann, ist ausschließlich an `127.0.0.1` (localhost)
gebunden — nach außen wird nichts freigegeben. Details siehe `docs/SECURITY.md` im Repository.

## Für Entwickler

```powershell
dotnet restore
dotnet test tests\KeryxNodeManager.Core.Tests\KeryxNodeManager.Core.Tests.csproj -c Release
dotnet run --project src\KeryxNodeManager.App -- --mock
```

`--mock` startet die Oberfläche mit virtuellen GPUs, ohne echte Keryx-Binärdateien oder NVAPI —
eine sichere Möglichkeit, die Oberfläche zu betrachten. Siehe `docs/BUILD.md` für Details zum
Build und `docs/RELEASE.md` für den Release-Prozess.

## Lizenz und Status

Aktiv entwickeltes, community-getragenes Projekt. Fehlerberichte und Vorschläge sind über Issues
willkommen.
