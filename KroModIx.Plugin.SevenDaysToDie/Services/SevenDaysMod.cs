using System;

namespace KroModIx.Plugin.SevenDaysToDie.Services;

/// <summary>Ein 7DTD-Mod im <c>&lt;InstallDir&gt;/Mods/</c>-Ordner. Jeder
/// Mod ist ein eigenes Verzeichnis mit einer <c>ModInfo.xml</c> im Root.
/// Toggle via <c>.disabled</c>-Suffix am Ordner-Namen (7DTD ignoriert
/// Ordner mit unbekannter Extension).
///
/// <para>XML-Felder aus ModInfo.xml (optional — kein Feld ist Pflicht,
/// aber Name/Version/Author sind Konvention). Wenn XML nicht lesbar ist,
/// wird der Ordner-Name als Fallback genommen und die anderen Felder
/// bleiben leer.</para></summary>
public sealed record SevenDaysMod(
    string Path,
    string FolderName,
    string DisplayName,
    string? Author,
    string? Version,
    string? Description,
    bool IsEnabled,
    long SizeBytes,
    DateTime InstalledUtc)
{
    /// <summary>v0.5.0: gesetzt, wenn dieser Eintrag einem <b>anderen
    /// Mod-Manager</b> gehört (lmm, r2modman, Vortex, SMM/ficsit). Erkannt am
    /// Verweis, nicht am Namen — siehe
    /// <see cref="KroModIx.Plugin.Contracts.ForeignManagerDetection"/>.
    /// Solche Einträge werden gelistet, damit der Nutzer sieht was im Spiel
    /// liegt, aber nicht verändert: am 04.10.2026 hat ein
    /// Deinstallieren-Klick im Icarus-Plugin lmms Pak entfernt und damit
    /// lautlos eine Mod aus dem Spiel genommen, während ihre Quelle woanders
    /// unversehrt lag.</summary>
    public string? ManagedBy { get; init; }

    /// <summary>Ob das Plugin diesen Eintrag verändern darf.</summary>
    public bool CanModify => ManagedBy is null;

    /// <summary>Einmalige Erkennung beim Scan — der Scanner legt sie über
    /// seine Ergebnisliste. Als Methode und nicht als berechnete Eigenschaft,
    /// weil sonst jede Bindung in der Oberfläche einen Dateisystem-Zugriff
    /// auslöst.</summary>
    public SevenDaysMod MitVerwalterErkennung()
        => KroModIx.Plugin.Contracts.ForeignManagerDetection.IsForeignManaged(Path, out var wer)
            ? this with { ManagedBy = wer }
            : this;
}

