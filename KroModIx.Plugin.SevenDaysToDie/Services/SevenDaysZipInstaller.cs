using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.SevenDaysToDie.Services;

/// <summary>Installiert ein Nexus-Mod-Archiv (ZIP/RAR/7z) fuer 7 Days to Die.
/// 7DTD-Mods sind IMMER Ordner unter <c>&lt;InstallDir&gt;/Mods/&lt;ModName&gt;/</c>
/// mit <c>ModInfo.xml</c> im Root. Auto-Layout-Detection:
/// <list type="bullet">
/// <item>Archiv enthaelt einen oder mehrere <c>ModInfo.xml</c>-Files:
/// jeder ModInfo.xml-Elternordner ist ein Mod, wird per Ordner unter
/// <c>Mods/</c> extrahiert (Original-Namensgebung erhalten).</item>
/// <item>Archiv enthaelt einen <c>Mods/&lt;X&gt;/</c>-Root-Ordner:
/// direktes Extract ins Game-Root (behaelt Mods/-Layout).</item>
/// <item>Sonst: Fehler „Kein 7DTD-Mod erkennbar".</item>
/// </list>
///
/// <para><b>Seit v0.2.0 über <c>IHostServices.Archives</c></b> (Host
/// v1.32.0). Welches Layout ein Archiv hat und wie der Mod-Ordner heißt,
/// bleibt 7DTD-Wissen; das Öffnen der Formate und der Ausbruch-Schutz kommen
/// aus dem Host. Der eigene Schutz prüfte an <b>zwei</b> Stellen
/// <c>Contains("..")</c> — das lässt einen absoluten Eintragsnamen durch,
/// und <c>Path.Combine</c> verwirft dann das Zielverzeichnis. Am
/// Cyberpunk-Installer, der dieselbe Prüfung trug, nachgewiesen: die Datei
/// landete außerhalb des Spiels und der Install meldete Erfolg.</para>
///
/// <para>Der Mod-Ordner je <c>ModInfo.xml</c> entsteht jetzt über
/// <see cref="ArchiveExtractOptions.StripPrefix"/> statt über eine eigene
/// Schleife mit Pfad-Arithmetik — genau der Fall, für den die Option im
/// Baukasten vorgesehen ist.</para></summary>
public sealed class SevenDaysZipInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly IArchiveService _archives;
    private readonly SevenDaysInstallManifestStore? _manifests;

    public SevenDaysZipInstaller(IArchiveService archives,
        SevenDaysInstallManifestStore? manifests = null)
    {
        _archives = archives;
        _manifests = manifests;
    }

    public SevenDaysZipInstallResult Install(string archivePath, DetectedGame game)
    {
        if (!File.Exists(archivePath))
            return SevenDaysZipInstallResult.Fail($"Archiv nicht gefunden: {archivePath}");
        var installDir = game.InstallDir;
        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            return SevenDaysZipInstallResult.Fail($"7DTD-InstallDir ungueltig: {installDir}");

        // v0.3.0: anlegen statt annehmen, und eine abweichende Schreibweise
        // findet ModFolderDiscovery mit — 7DTD laedt beide.
        var modsRoot = ModFolderDiscovery.FindOrCreate(installDir, "Mods", "mods")
                       ?? Path.Combine(installDir, "Mods");
        Directory.CreateDirectory(modsRoot);

        try
        {
            // Am Inhalt pruefen, nicht an der Endung: ein Download mit
            // falscher Endung landete sonst unveraendert im Spiel.
            if (_archives.DetectKind(archivePath) == ArchiveKind.Unknown)
                return SevenDaysZipInstallResult.Fail(
                    "Das ist kein lesbares Archiv (ZIP/RAR/7z) — eventuell ein abgebrochener Download.");

            var entries = _archives.List(archivePath);
            if (entries.Count == 0)
                return SevenDaysZipInstallResult.Fail("Archiv ist leer.");

            // 1) Archiv enthaelt bereits das Mods/-Layout auf Root-Ebene.
            var directLayout = entries.Any(e =>
                e.Path.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase));
            if (directLayout)
            {
                var r = _archives.Extract(archivePath, installDir);
                if (Abgelehnt(r) is { } warnung)
                    return new SevenDaysZipInstallResult(false, warnung,
                        r.ExtractedPaths, Array.Empty<string>());
                var modFolders = InferModFolders(r.ExtractedPaths, modsRoot);
                WriteManifests(modFolders, archivePath);
                return SevenDaysZipInstallResult.Ok(
                    $"Direkt-Layout: {r.Count} Datei(en) ins Game-Root extrahiert.",
                    r.ExtractedPaths, modFolders);
            }

            // 2) Kein Mods/-Root, aber irgendwo ModInfo.xml.
            var modInfoEntries = entries
                .Where(e => e.Path.EndsWith("/ModInfo.xml", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(e.Path, "ModInfo.xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (modInfoEntries.Count == 0)
                return SevenDaysZipInstallResult.Fail(
                    "Archiv enthaelt weder Mods/-Ordner noch ModInfo.xml — kein 7DTD-Mod erkennbar.");

            // Fuer jede ModInfo.xml den enthaltenden Ordner-Prefix ableiten und
            // alle Files unter diesem Prefix nach Mods/<ordnerName>/ extrahieren.
            var installedAll = new List<string>();
            var modFoldersSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var abgelehnt = new List<string>();
            foreach (var mi in modInfoEntries)
            {
                string? prefix;
                string folderName;
                var slash = mi.Path.LastIndexOf('/');
                if (slash < 0)
                {
                    // ModInfo.xml direkt im Archiv-Root — Ordner-Name aus Archiv-Filename.
                    prefix = null;
                    folderName = SanitizeFolder(Path.GetFileNameWithoutExtension(archivePath));
                }
                else
                {
                    prefix = mi.Path[..slash];
                    folderName = new DirectoryInfo(prefix).Name;
                }
                var target = Path.Combine(modsRoot, folderName);
                Directory.CreateDirectory(target);
                modFoldersSet.Add(target);

                var r = _archives.Extract(archivePath, target,
                    new ArchiveExtractOptions(StripPrefix: prefix));
                installedAll.AddRange(r.ExtractedPaths);
                abgelehnt.AddRange(r.SkippedUnsafe);
            }

            if (abgelehnt.Count > 0)
                return new SevenDaysZipInstallResult(false,
                    Meldung(abgelehnt, installedAll.Count), installedAll,
                    modFoldersSet.ToList());

            WriteManifests(modFoldersSet.ToList(), archivePath);
            return SevenDaysZipInstallResult.Ok(
                $"ModInfo-Layout: {modFoldersSet.Count} Mod-Ordner extrahiert.",
                installedAll, modFoldersSet.ToList());
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Install fehlgeschlagen: {Archive}", archivePath);
            return SevenDaysZipInstallResult.Fail($"Fehler: {ex.Message}");
        }
    }

    /// <summary>Hat der Ausbruch-Schutz Einträge abgelehnt, bricht der
    /// Install mit Meldung ab — statt still das zu installieren, was
    /// durchkam. Das trifft auch ein bloß kaputtes Archiv mit einem krummen
    /// Eintrag unter zweihundert; bewusst, denn ein Archiv, das aus dem
    /// Spielverzeichnis herausschreiben will, ist nicht „überwiegend in
    /// Ordnung", und die Entscheidung gehört dem Nutzer.</summary>
    private static string? Abgelehnt(ArchiveExtractResult r)
        => r.SkippedUnsafe.Count == 0 ? null : Meldung(r.SkippedUnsafe, r.Count);

    private static string Meldung(IReadOnlyList<string> abgelehnt, int geschrieben)
    {
        Log.Warn("Ausbruchsversuch im Archiv, {Count} Eintrag/Einträge abgelehnt: {Entries}",
            abgelehnt.Count, string.Join(", ", abgelehnt));
        return $"Abgebrochen: {abgelehnt.Count} Eintrag/Einträge wollten aus dem "
             + "Spielverzeichnis herausschreiben — "
             + string.Join(", ", abgelehnt.Take(3))
             + (abgelehnt.Count > 3 ? ", …" : "")
             + $". {geschrieben} Datei(en) waren schon geschrieben, bevor das auffiel.";
    }

    private static string SanitizeFolder(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>Aus einer flachen Liste installierter Files die Mod-Ordner
    /// (direkte Unterordner von Mods/) ableiten. Fuer WriteManifests: wir
    /// wollen pro Mod-Ordner genau EIN Manifest, nicht pro einzelnem File.</summary>
    private static IReadOnlyList<string> InferModFolders(
        IReadOnlyList<string> installedPaths, string modsRoot)
    {
        var norm = Path.GetFullPath(modsRoot);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in installedPaths)
        {
            var full = Path.GetFullPath(p);
            if (!full.StartsWith(norm, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = full.Substring(norm.Length).TrimStart(Path.DirectorySeparatorChar);
            var sep = rel.IndexOf(Path.DirectorySeparatorChar);
            if (sep < 0) continue;
            folders.Add(Path.Combine(modsRoot, rel[..sep]));
        }
        return folders.ToList();
    }

    /// <summary>Fuer jeden installierten Mod-Ordner ein Manifest speichern.
    /// ModId + Version aus dem Nexus-CDN-Filename (Dash- oder Space-Format) —
    /// sonst leer, dann kein Update-Discovery moeglich fuer diesen Mod.</summary>
    private void WriteManifests(IReadOnlyList<string> modFolderPaths, string archivePath)
    {
        if (_manifests is null) return;
        var archiveName = Path.GetFileName(archivePath);
        var nexusModId = NexusFileNameParser.TryExtractModId(archiveName);
        var nexusVersion = NexusFileNameParser.TryExtractVersion(archiveName);
        foreach (var folder in modFolderPaths)
        {
            var folderName = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(folderName)) continue;
            var key = SevenDaysInstallManifestStore.BuildKey(folderName);
            _manifests.Save(key, new SevenDaysInstallManifest(
                NexusModId: nexusModId,
                OriginalFilename: archiveName,
                NexusVersion: nexusVersion,
                InstalledAtUtc: DateTime.UtcNow));
        }
    }

    /// <summary>Endungs-Vorfilter fuer den Downloads-Tab. Kommt aus dem
    /// Host-Baukasten, damit ein dort neu unterstuetztes Format nicht in
    /// neun Plugins nachgetragen werden muss.</summary>
    public IReadOnlyList<string> SupportedExtensions => _archives.SupportedExtensions;

    public bool HasSupportedExtension(string path) => _archives.HasSupportedExtension(path);
}

public sealed record SevenDaysZipInstallResult(
    bool Success,
    string Message,
    IReadOnlyList<string> InstalledPaths,
    IReadOnlyList<string> ModFolderPaths)
{
    public static SevenDaysZipInstallResult Ok(string msg,
        IReadOnlyList<string> paths, IReadOnlyList<string> folders) =>
        new(true, msg, paths, folders);
    public static SevenDaysZipInstallResult Fail(string msg) =>
        new(false, msg, Array.Empty<string>(), Array.Empty<string>());
}
