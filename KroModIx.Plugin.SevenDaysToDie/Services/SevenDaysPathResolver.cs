using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.SevenDaysToDie.Services;

/// <summary>Liefert den <c>&lt;InstallDir&gt;/Mods/</c>-Pfad. 7 Days to Die
/// hat einen Vanilla-Mod-Loader (kein BepInEx, kein MelonLoader) — der
/// Ordner muss nur existieren, bei Erst-Install legen wir ihn an. Ab A20
/// sind Mods per Default aktiv (kein serverconfig.xml-EnableMods-Flag
/// mehr noetig).</summary>
public sealed class SevenDaysPathResolver
{
    private static readonly string[] ModDirCandidates = { "Mods", "mods" };

    /// <summary>Der vorhandene Mods-Ordner, oder null. Legt nichts an.
    /// v0.3.0: findet unter Linux auch ein abweichend geschriebenes
    /// <c>mods/</c> — 7DTD selbst laedt beide Schreibweisen.</summary>
    public string? GetModsDir(DetectedGame game)
        => string.IsNullOrEmpty(game.InstallDir)
            ? null
            : ModFolderDiscovery.Find(game.InstallDir, ModDirCandidates);

    /// <summary>Legt <c>&lt;InstallDir&gt;/Mods/</c> an falls keine Variante
    /// existiert. Idempotent; null nur wenn das Anlegen scheitert.</summary>
    public string? EnsureModsDir(DetectedGame game)
        => string.IsNullOrEmpty(game.InstallDir)
            ? null
            : ModFolderDiscovery.FindOrCreate(game.InstallDir, ModDirCandidates);
}
