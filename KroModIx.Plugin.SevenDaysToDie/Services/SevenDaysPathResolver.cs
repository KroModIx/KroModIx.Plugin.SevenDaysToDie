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
    public string GetModsDir(DetectedGame game)
        => Path.Combine(game.InstallDir, "Mods");

    /// <summary>Legt <c>&lt;InstallDir&gt;/Mods/</c> an falls noch nicht
    /// vorhanden. Aufruf idempotent.</summary>
    public string EnsureModsDir(DetectedGame game)
    {
        var dir = GetModsDir(game);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
