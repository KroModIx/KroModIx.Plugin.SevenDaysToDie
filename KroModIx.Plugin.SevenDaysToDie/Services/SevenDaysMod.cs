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
    DateTime InstalledUtc);
