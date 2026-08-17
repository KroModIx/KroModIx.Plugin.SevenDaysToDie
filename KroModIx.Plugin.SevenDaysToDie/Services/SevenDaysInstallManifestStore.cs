using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.SevenDaysToDie.Services;

/// <summary>Persistiert pro installiertem 7DTD-Mod-Ordner ein Manifest-File
/// mit dem Nexus-Match-Kontext (ModId + Version + Original-Filename).
/// Ohne diesen Store haette der Installiert-Tab keine Basis fuer
/// Update-Discovery — ModInfo.xml hat kein „NexusModId"-Feld.
///
/// <para>Layout:</para>
/// <code>
/// ~/.config/KroModIx/plugin-data/kroste.sevendaystodie/install-manifests/
/// ├── MyMod.json
/// ├── AnotherMod.json
/// └── …
/// </code>
///
/// <para>Manifest-Key = Ordner-Name (ohne .disabled-Suffix, damit Toggle
/// den Key nicht bricht).</para></summary>
public sealed class SevenDaysInstallManifestStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _dir;

    public SevenDaysInstallManifestStore(IHostServices host)
    {
        _dir = Path.Combine(host.PluginDataDir, "install-manifests");
        Directory.CreateDirectory(_dir);
    }

    public static string BuildKey(string modName) => SanitizeFileName(modName);

    private static string SanitizeFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return sb.ToString();
    }

    public SevenDaysInstallManifest? TryGet(string key)
    {
        var path = Path.Combine(_dir, key + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<SevenDaysInstallManifest>(File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InstallManifest unlesbar: {Path}", path);
            return null;
        }
    }

    public void Save(string key, SevenDaysInstallManifest manifest)
    {
        try
        {
            File.WriteAllText(Path.Combine(_dir, key + ".json"),
                JsonSerializer.Serialize(manifest, JsonOpts));
        }
        catch (Exception ex) { Log.Warn(ex, "InstallManifest-Save fehlgeschlagen: {Key}", key); }
    }

    public void Delete(string key)
    {
        var path = Path.Combine(_dir, key + ".json");
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Debug(ex, "InstallManifest-Delete fehlgeschlagen: {Path}", path); }
    }

    public IReadOnlyList<(string Key, SevenDaysInstallManifest Manifest)> LoadAll()
    {
        if (!Directory.Exists(_dir)) return Array.Empty<(string, SevenDaysInstallManifest)>();
        var list = new List<(string, SevenDaysInstallManifest)>();
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
        {
            var key = Path.GetFileNameWithoutExtension(f);
            var m = TryGet(key);
            if (m is not null) list.Add((key, m));
        }
        return list;
    }
}

public sealed record SevenDaysInstallManifest(
    int? NexusModId,
    string? OriginalFilename,
    string? NexusVersion,
    DateTime InstalledAtUtc);
