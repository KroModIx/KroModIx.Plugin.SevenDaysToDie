using System.IO;
using NLog;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.SevenDaysToDie.Services;

/// <summary>Toggle + Uninstall fuer 7DTD-Mods. Jeder Mod ist ein Ordner
/// unter <c>&lt;InstallDir&gt;/Mods/</c>. Enable/Disable via
/// <c>.disabled</c>-Suffix am Ordner-Namen — 7DTD ignoriert Ordner mit
/// unbekannter Extension. Reversibel, kein Datei-Verlust.</summary>
public sealed class SevenDaysInstallService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Wirft, wenn der Eintrag einem fremden Mod-Manager gehört. Die
    /// Meldung nennt Verwalter, Folge und Ausweg — am 04.10.2026 hat genau so
    /// ein Löschen im Icarus-Plugin eine Mod aus dem Spiel genommen, ohne dass
    /// es auffiel, weil die Quelle woanders unversehrt lag.</summary>
    private static void NurWennUnser(SevenDaysMod mod, string verb)
    {
        if (mod.CanModify) return;
        throw new InvalidOperationException(
            ForeignManagerDetection.Meldung(mod.DisplayName, mod.ManagedBy, verb));
    }

    public string SetEnabled(SevenDaysMod mod, bool enable)
    {
        NurWennUnser(mod, "umschalten");
        var path = mod.Path;
        var parent = Path.GetDirectoryName(path)!;
        var name = new DirectoryInfo(path).Name;
        string newPath;
        if (enable)
        {
            if (!name.EndsWith(".disabled")) return path;
            var trimmed = name[..^".disabled".Length];
            newPath = Path.Combine(parent, trimmed);
        }
        else
        {
            if (name.EndsWith(".disabled")) return path;
            newPath = path + ".disabled";
        }
        if (Directory.Exists(newPath)) Directory.Delete(newPath, recursive: true);
        Directory.Move(path, newPath);
        Log.Info("Toggle-Dir: {From} -> {To}", path, newPath);
        return newPath;
    }

    public void Uninstall(SevenDaysMod mod)
    {
        NurWennUnser(mod, "deinstallieren");
        if (Directory.Exists(mod.Path)) Directory.Delete(mod.Path, recursive: true);
        Log.Info("Uninstall: {Path}", mod.Path);
    }
}
