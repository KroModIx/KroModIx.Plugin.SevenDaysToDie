# KroModIx.Plugin.SevenDaysToDie

[![CI](https://github.com/KroModIx/KroModIx.Plugin.SevenDaysToDie/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx.Plugin.SevenDaysToDie/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx.Plugin.SevenDaysToDie)](https://github.com/KroModIx/KroModIx.Plugin.SevenDaysToDie/releases)

**7 Days to Die Mod-Manager** — Plugin für den
[KroModIx](https://github.com/KroModIx/KroModIx).

Verwaltet Vanilla-Loader-Mods für 7 Days to Die (The Fun Pimps,
Steam AppId 251570). Jeder Mod ist ein Ordner mit `ModInfo.xml` unter
`<InstallDir>/Mods/`. Kein Bootstrap-Assistent nötig — der Mod-Loader
ist Vanilla, ab A20 sind Mods per Default aktiv.

## Voraussetzungen

Braucht den [KroModIx-Host](https://github.com/KroModIx/KroModIx) **ab
v1.32.0** — dort sitzen der Backup-Baukasten, der gemeinsame
Versions-Vergleich und seit v0.4.0 der Archiv-Baukasten samt Ausbruch-Schutz,
gegen die dieses Plugin gebaut ist. Ältere Hosts laden das Plugin nicht.

## Screenshot

![Nexus-Katalog mit Voll-Katalog-Suche](docs/screenshot.png)

## Features

### Installiert-Tab

- **Discovery** aller Mod-Ordner unter `<InstallDir>/Mods/`:

  | Zustand | Ordner | Toggle |
  |---|---|---|
  | Aktiv | `Mods/MyMod/` | Rename → `Mods/MyMod.disabled/` |
  | Deaktiviert | `Mods/MyMod.disabled/` | Rename zurück |

- **ModInfo.xml-Parser** unterstützt beide historischen Formate:
  - A20+ Attribut-Form: `<Name value="Foo" />`, `<Author value="X" />` …
  - Legacy Element-Form: `<Name>Foo</Name>`, `<Author>X</Author>` …
- **Kroste-Card-Row** mit Cover (140×90, aus Nexus-Katalog via
  InstallManifest-ModId), Titel, Autor · Version · Größe · Datum,
  Summary, Status-Label, Actions (Toggle, 🔍 Details, 🗑 Deinstallieren).
- **Doppelklick auf Row** öffnet den gleichen Nexus-Detail-Dialog wie
  im Katalog-Tab (falls ModId im InstallManifest hinterlegt).
- **Bulk-Aktionen** mit Progress-Scope: „▶▶ Alle aktivieren" / „⏸⏸ Alle
  deaktivieren" mit Confirm-Dialog.
- **Filter-Textbox** live nach Namen.

### Nexus-Tab

- **Voll-Katalog** via `INexusService.SearchModsAsync` (Contracts v1.15+,
  öffentliches GraphQL, kein Personal-Key nötig für Read).
- Pagination (40 pro Seite), Sort (Update / Add / Endorsed / Downloaded),
  Server-Search, Kategorie-Filter clientseitig.
- **Direct-Download** in den Plugin-Downloads-Ordner (Premium-Only, analog
  Cyberpunk/DSP).
- **Detail-Dialog** mit Cover, Meta-Row, KI-Zusammenfassung
  (via `IHostServices.Ai`), Rich-HTML-Beschreibung. Sprachabhängiger
  AI-Prompt.

### Downloads-Tab

- Listet `.zip` / `.rar` / `.7z` im Plugin-Downloads-Ordner.
- **Auto-Layout-Install** via SharpCompress:
  - Archiv mit `Mods/`-Root → direktes Extract ins Game-Root
  - Archiv mit `ModInfo.xml` irgendwo → Elternordner wird zum Mod
- **Bulk-Install** aller Downloads mit Progress-Scope.
- **Row-Enrichment**: `NexusFileNameParser` extrahiert ModId aus dem
  Nexus-CDN-Filename (beide Formate: Dash + Space), Enricher zieht
  Cover/Autor/Summary aus dem Katalog. Doppelklick + 🔍 Details.

### Update-Discovery (IUpdateNotifier)

- `SevenDaysInstallManifestStore` persistiert pro installiertem
  Mod-Ordner ein Manifest mit Nexus-ModId + Version + Original-Filename.
- `SevenDaysUpdateChecker` matcht Manifests gegen Nexus-Katalog,
  meldet echte Versions-Deltas als grünen ↑-Badge auf der 7DTD-
  Sidebar-Kachel.

### Sprachumschaltung

DE + EN. Nach Sprachwechsel im Host: Kachel neu selektieren, dann sind
die frischen Übersetzungen aktiv (Host-Tab-Cache-Invalidate seit v1.14.7).

## Build

```bash
dotnet build -c Release
dotnet test
```

## Backups vor jedem Install

Bevor das Plugin Dateien ins Spiel schreibt, legt es einen Snapshot des
Ziel-Verzeichnisses an — bei Einzel-Installs einen pro Mod, bei Bulk-Installs
**einen** vor dem ganzen Durchlauf. Zurückspielen läuft über das
Backups-Fenster im Kontextmenü der Sidebar-Kachel; es gibt bewusst kein
Auto-Rollback, damit du entscheidest, welchen Stand du zurückholst.
Aufbewahrt werden die letzten zehn Snapshots pro Spiel.

Schlägt ein Snapshot fehl, läuft der Install trotzdem durch (mit Log-Eintrag)
— das Backup ist ein Netz, kein Türsteher.

## Lizenz

MIT — siehe [LICENSE](LICENSE).
