# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Grundlagen

- **Was:** Mod-Manager für 7 Days to Die (The Fun Pimps) als KroModIx-Plugin. Steam-AppId **251570**, Plugin-Id `kroste.sevendaystodie`.
- **Stack:** .NET 10, `KroModIx.Plugin.Contracts` als PackageReference, `minHostVersion` 1.27.0.
- **Repo:** `github.com/KroModIx/KroModIx.Plugin.SevenDaysToDie`.
- **Deploy-Ziel:** `~/.config/KroModIx/plugins/kroste.sevendaystodie/`.
- **Datenquelle:** Nexus Mods, Game-Slug **`7daystodie`** (`SevenDaysNexusCatalog.GameSlug`).
- **Kroste-Standards:** `~/.claude/skills/KroModIx-Plugin/`. Hier steht nur, was 7DTD-spezifisch ist.

## Kein Loader — das unterscheidet dieses Plugin von DSP und Schedule I

7DTD lädt Mods **vanilla**, ohne BepInEx oder MelonLoader. Es gibt deshalb bewusst **keinen Bootstrapper** in diesem Repo. Wer einen sucht, weil DSP und Schedule I einen haben: er fehlt absichtlich.

- Mod-Verzeichnis: `<InstallDir>/Mods`
- **Ein Mod ist ein Ordner**, kein Archiv und keine DLL — erkannt an der `ModInfo.xml` darin.
- **Enable/Disable per Ordner-Rename** mit `.disabled`-Suffix. Dieses Plugin kann als einziges der Nexus-Plugins tatsächlich einzelne Mods abschalten.
- **Backup-Ziel:** `<InstallDir>/Mods/`.

## ModInfo.xml kommt in zwei Formaten

`SevenDaysModScanner` parst beide, und das muss so bleiben — die Community hat den Umstieg nie vollständig vollzogen:

- **A20+**: Werte als **Attribute** (`<Name value="…" />`)
- **älter**: Werte als **Elementinhalt**

Wer den Parser anfasst, braucht Fixtures für beide Varianten. Ein Mod mit altem Format fällt sonst still aus der Liste.

## Architektur

- **Services/**: `SevenDaysPathResolver` / `SevenDaysPaths`, `SevenDaysModScanner` (ModInfo.xml, beide Formate), `SevenDaysNexusCatalog` (GraphQL-Vollkatalog), `SevenDaysNexusRowEnricher`, `NexusFileNameParser`, `SevenDaysInstallManifestStore`, `SevenDaysInstallService` / `SevenDaysZipInstaller` (SharpCompress mit Auto-Layout-Detection: `Mods/`-Root im Archiv oder `ModInfo.xml`-Erkennung), `SevenDaysDownloader`, `SevenDaysUpdateChecker`, `CoverCache`, `DownloadEventBus`.
- **Views/**: Nexus (Katalog), Downloads, Installiert, `NexusModDetailWindow` + VM, `SevenDaysNexusDetailLauncher`.

## Bekannte Grenzen

- **Kein Dependency-Resolver.**
- **Keine Prüfung der Spielversion.** 7DTD bricht Mods zwischen Alpha-Versionen regelmäßig; das Plugin warnt nicht davor.
- Der Manifest-GC (v0.1.1, portiert aus DSP v0.6.4) bildet seinen Key über den Mod-Ordnernamen. Wer den Installer auf eine andere Key-Quelle umstellt, muss den GC-Callback im selben Commit mitziehen.
