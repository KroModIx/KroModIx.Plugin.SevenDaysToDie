using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.SevenDaysToDie.Services;

/// <summary>Enumeriert die 7DTD-Mods unter <c>&lt;InstallDir&gt;/Mods/</c>.
/// Ein Mod ist ein Ordner mit <c>ModInfo.xml</c> im Root. Ein Ordner mit
/// <c>.disabled</c>-Suffix zaehlt als deaktiviert. Fehlende ModInfo.xml
/// gibt <see cref="SevenDaysMod"/> mit Fallback-Werten (Name = Ordner-
/// Name, alle anderen Felder leer) — der User sieht den Mod trotzdem und
/// kann ihn deinstallieren, statt ihn stumm zu verschlucken.</summary>
public sealed class SevenDaysModScanner
{
    private readonly SevenDaysPathResolver _paths;

    public SevenDaysModScanner(SevenDaysPathResolver paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<SevenDaysMod> Scan(DetectedGame game)
    {
        var modsDir = _paths.GetModsDir(game);
        if (!Directory.Exists(modsDir)) return Array.Empty<SevenDaysMod>();

        var mods = new List<SevenDaysMod>();
        foreach (var dir in Directory.EnumerateDirectories(modsDir))
        {
            try
            {
                var name = Path.GetFileName(dir);
                var isEnabled = !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var displayFolder = isEnabled ? name : name[..^".disabled".Length];

                var xmlPath = Path.Combine(dir, "ModInfo.xml");
                string display = displayFolder;
                string? author = null, version = null, description = null;
                if (File.Exists(xmlPath))
                    (display, author, version, description) = ParseModInfo(xmlPath, displayFolder);

                long size = 0;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        try { size += new FileInfo(f).Length; } catch { }
                }
                catch { }

                var installed = new DirectoryInfo(dir).CreationTimeUtc;
                mods.Add(new SevenDaysMod(
                    Path: dir,
                    FolderName: displayFolder,
                    DisplayName: display,
                    Author: author,
                    Version: version,
                    Description: description,
                    IsEnabled: isEnabled,
                    SizeBytes: size,
                    InstalledUtc: installed));
            }
            catch { /* skip individual broken folders */ }
        }
        return mods.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Parst ModInfo.xml. Toleriert beide historischen 7DTD-
    /// Formate: A20+ (Attribut-basiert: <c>&lt;Name value="Foo"/&gt;</c>)
    /// und aeltere (Element-Text: <c>&lt;Name&gt;Foo&lt;/Name&gt;</c>).</summary>
    public static (string Name, string? Author, string? Version, string? Description) ParseModInfo(
        string xmlPath, string fallbackName)
    {
        try
        {
            var doc = XDocument.Load(xmlPath);
            var root = doc.Root;
            if (root is null) return (fallbackName, null, null, null);

            string? Field(string tag)
            {
                var el = root.Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, tag, StringComparison.OrdinalIgnoreCase));
                if (el is null) return null;
                var attr = el.Attribute("value") ?? el.Attribute("Value");
                var raw = attr?.Value ?? el.Value;
                return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            }

            var name = Field("Name") ?? Field("DisplayName") ?? fallbackName;
            var author = Field("Author");
            var version = Field("Version");
            var desc = Field("Description");
            return (name, author, version, desc);
        }
        catch { return (fallbackName, null, null, null); }
    }
}
