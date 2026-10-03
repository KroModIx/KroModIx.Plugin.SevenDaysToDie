using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;
using SharpCompress.Archives;

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
/// </list></summary>
public sealed class SevenDaysZipInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly SevenDaysInstallManifestStore? _manifests;

    public SevenDaysZipInstaller(SevenDaysInstallManifestStore? manifests = null)
    {
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
            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
                .ToList();
            if (entries.Count == 0)
                return SevenDaysZipInstallResult.Fail("Archiv ist leer.");

            var normalized = entries
                .Select(e => (Entry: e, Key: (e.Key ?? "").Replace('\\', '/')))
                .ToList();

            // 1) Archiv enthaelt bereits das Mods/-Layout auf Root-Ebene.
            var directLayout = normalized.Any(x =>
                x.Key.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase));
            if (directLayout)
            {
                var installed = ExtractDirect(normalized, installDir);
                var modFolders = InferModFolders(installed, modsRoot);
                WriteManifests(modFolders, archivePath);
                return SevenDaysZipInstallResult.Ok(
                    $"Direkt-Layout: {installed.Count} Datei(en) ins Game-Root extrahiert.",
                    installed, modFolders);
            }

            // 2) Kein Mods/-Root, aber irgendwo ModInfo.xml.
            var modInfoEntries = normalized
                .Where(x => x.Key.EndsWith("/ModInfo.xml", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(x.Key, "ModInfo.xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (modInfoEntries.Count == 0)
                return SevenDaysZipInstallResult.Fail(
                    "Archiv enthaelt weder Mods/-Ordner noch ModInfo.xml — kein 7DTD-Mod erkennbar.");

            // Fuer jede ModInfo.xml den enthaltenden Ordner-Prefix ableiten und
            // alle Files unter diesem Prefix nach Mods/<ordnerName>/ extrahieren.
            var installedAll = new List<string>();
            var modFoldersSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mi in modInfoEntries)
            {
                string prefix, folderName;
                var slash = mi.Key.LastIndexOf('/');
                if (slash < 0)
                {
                    // ModInfo.xml direkt im Archiv-Root — Ordner-Name aus Archiv-Filename.
                    prefix = "";
                    folderName = SanitizeFolder(Path.GetFileNameWithoutExtension(archivePath));
                }
                else
                {
                    prefix = mi.Key.Substring(0, slash + 1);
                    folderName = new DirectoryInfo(prefix.TrimEnd('/')).Name;
                }
                var target = Path.Combine(modsRoot, folderName);
                Directory.CreateDirectory(target);
                modFoldersSet.Add(target);

                foreach (var x in normalized.Where(x => x.Key.StartsWith(prefix,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    var rel = prefix.Length == 0 ? x.Key : x.Key.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(rel) || rel.EndsWith("/")) continue;
                    if (rel.Contains("..")) { Log.Warn("Zip-Slip: {N}", rel); continue; }
                    var dst = Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    ExtractOne(x.Entry, dst);
                    installedAll.Add(dst);
                }
            }
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

    private static string SanitizeFolder(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static IReadOnlyList<string> ExtractDirect(
        IReadOnlyList<(IArchiveEntry Entry, string Key)> entries, string installDir)
    {
        var installed = new List<string>();
        foreach (var (entry, key) in entries)
        {
            if (string.IsNullOrEmpty(key) || key.EndsWith('/')) continue;
            if (key.Contains("..")) { Log.Warn("Zip-Slip: {N}", key); continue; }
            var dst = Path.Combine(installDir, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            ExtractOne(entry, dst);
            installed.Add(dst);
        }
        return installed;
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

    private static void ExtractOne(IArchiveEntry entry, string destination)
    {
        using var input = entry.OpenEntryStream();
        using var output = File.Create(destination);
        input.CopyTo(output);
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

    public static readonly string[] SupportedExtensions = new[] { ".zip", ".rar", ".7z" };
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
