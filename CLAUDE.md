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

## Archive kommen aus dem Host (ab v0.4.0)

`SevenDaysZipInstaller` bekommt `IHostServices.Archives` eingespritzt;
SharpCompress ist aus dem Plugin verschwunden. Welches Layout ein Archiv hat
und wie der Mod-Ordner heißt, bleibt 7DTD-Wissen.

**Der eigene Schutz stand an zwei Stellen** und prüfte beide Male
`Contains("..")`. Das lässt einen absoluten Eintragsnamen durch, und
`Path.Combine` verwirft dann das Zielverzeichnis. Am Cyberpunk-Installer, der
dieselbe Prüfung trug, am 03.10.2026 nachgewiesen: die Datei landete außerhalb
des Spiels, und der Install meldete `Success = true`.

**Der Mod-Ordner je `ModInfo.xml` entsteht jetzt über
`ArchiveExtractOptions.StripPrefix`** statt über eine eigene Schleife mit
Pfad-Arithmetik — genau der Fall, für den die Option im Baukasten vorgesehen
ist.

**Ein Nebenbefund, der beim Testen auffiel und nicht offensichtlich ist:**
in diesem Zweig fängt schon der Präfix-Filter einen Fremdeintrag ab — er
gehört nicht unter das Präfix und wird gar nicht zum Auspacken angeboten. Der
Install läuft dann **durch**, denn hier wurde nichts abgewehrt, sondern nur
ausgewählt. Der Ausbruch-Schutz greift in diesem Zweig nur, wenn die
`ModInfo.xml` im Archiv-Wurzelverzeichnis liegt, weil dann kein Präfix
gesetzt wird. Beide Fälle stehen als getrennte Tests
(`Unter_einem_Praefix_filtert_schon_die_Auswahl` und
`Absoluter_Eintragsname_bricht_auch_im_ModInfo_Zweig_nicht_aus`) — ein
einziger Test hätte hier leicht das Falsche belegt.

**Der Installer hatte bis v0.4.0 keine Tests.** Jetzt elf: beide Layouts,
mehrere Mods in einem Archiv, `ModInfo.xml` im Wurzelverzeichnis, beide
Ausbruch-Zweige, die Inhaltsprüfung und der Endungs-Vorfilter. Die Tests
nutzen `KroModIx.Plugin.TestKit`; der Ausbruch-Schutz darin ist **nicht**
nachgebaut, sondern dieselbe Funktion `ArchivePathSafety` aus den Contracts.

**Ein Ausbruchsversuch bricht den Install ab**, statt still das zu
installieren, was durchkam — und die Meldung nennt, was schon geschrieben
wurde, damit der Nutzer weiß, was er aufräumen soll.
