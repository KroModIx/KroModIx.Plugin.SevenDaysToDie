using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.SevenDaysToDie.Services;
using KroModIx.Plugin.TestKit;
using Xunit;

namespace KroModIx.Plugin.SevenDaysToDie.Tests;

/// <summary>Der Installer hatte bis v0.2.0 (2026-10-03) <b>keine</b> Tests —
/// und zwei eigene Kopien eines Ausbruch-Schutzes, der nur auf <c>..</c>
/// prüfte. Beides hier nachgeholt: die Layout-Erkennung, die das Plugin
/// verantwortet, und der Fall, an dem die alte Prüfung vorbeiging.</summary>
public sealed class ZipInstallerTests : IDisposable
{
    private readonly string _tmp;
    private readonly string _installRoot;
    private readonly DetectedGame _game;
    private readonly FakeArchiveService _archives = new();
    private readonly SevenDaysZipInstaller _installer;

    public ZipInstallerTests()
    {
        _installer = new SevenDaysZipInstaller(_archives);
        _tmp = Directory.CreateTempSubdirectory("kromodix-7dtd-zip").FullName;
        _installRoot = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_installRoot);

        _game = new DetectedGame(
            Target: new GameTarget("7-days-to-die", "7 Days to Die", 251570,
                Array.Empty<string>(), Platforms.Both),
            InstallDir: _installRoot,
            UserDataDir: null,
            ProtonPrefix: null,
            Runtime: RuntimeKind.Native,
            Source: GameSource.Steam);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string BuildZip(string name, params (string Path, string Content)[] entries)
    {
        var zipPath = Path.Combine(_tmp, name);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (path, content) in entries)
        {
            using var s = archive.CreateEntry(path).Open();
            using var sw = new StreamWriter(s);
            sw.Write(content);
        }
        return zipPath;
    }

    private const string ModInfo = """<?xml version="1.0"?><xml><Name value="MeinMod" /></xml>""";

    [Fact]
    public void Direkt_Layout_behaelt_den_Mods_Ordner()
    {
        var zip = BuildZip("mod.zip",
            ("Mods/MeinMod/ModInfo.xml", ModInfo),
            ("Mods/MeinMod/Config/blocks.xml", "<blocks/>"));

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeTrue(r.Message);
        File.Exists(Path.Combine(_installRoot, "Mods", "MeinMod", "ModInfo.xml")).Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "Mods", "MeinMod", "Config", "blocks.xml"))
            .Should().BeTrue();
        r.ModFolderPaths.Should().ContainSingle()
            .Which.Should().EndWith(Path.Combine("Mods", "MeinMod"));
    }

    /// <summary>Der häufigste Nexus-Fall: ein Ordner mit dem Mod-Namen, die
    /// <c>ModInfo.xml</c> darin, und kein <c>Mods/</c> im Archiv. Der Ordner
    /// entsteht seit v0.2.0 über <c>StripPrefix</c> des Host-Baukastens statt
    /// über eine eigene Schleife mit Pfad-Arithmetik.</summary>
    [Fact]
    public void ModInfo_Layout_legt_den_Ordner_unter_Mods_an()
    {
        var zip = BuildZip("mod.zip",
            ("ZombieTweaks/ModInfo.xml", ModInfo),
            ("ZombieTweaks/Config/entityclasses.xml", "<e/>"),
            ("liesmich.txt", "Anleitung"));

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeTrue(r.Message);
        File.Exists(Path.Combine(_installRoot, "Mods", "ZombieTweaks", "ModInfo.xml"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "Mods", "ZombieTweaks", "Config", "entityclasses.xml"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "Mods", "ZombieTweaks", "liesmich.txt"))
            .Should().BeFalse("was außerhalb des Mod-Ordners liegt, gehört nicht in den Mod");
    }

    [Fact]
    public void Mehrere_ModInfo_werden_zu_mehreren_Ordnern()
    {
        var zip = BuildZip("paket.zip",
            ("ModA/ModInfo.xml", ModInfo),
            ("ModB/ModInfo.xml", ModInfo),
            ("ModB/Config/x.xml", "<x/>"));

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeTrue(r.Message);
        r.ModFolderPaths.Should().HaveCount(2);
        File.Exists(Path.Combine(_installRoot, "Mods", "ModA", "ModInfo.xml")).Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "Mods", "ModB", "Config", "x.xml")).Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "Mods", "ModA", "Config", "x.xml"))
            .Should().BeFalse("die Ordner dürfen sich nicht vermischen");
    }

    /// <summary>Liegt die <c>ModInfo.xml</c> im Archiv-Wurzelverzeichnis,
    /// gibt es keinen Ordnernamen — dann kommt er aus dem Archiv-Dateinamen.</summary>
    [Fact]
    public void ModInfo_im_Wurzelverzeichnis_nimmt_den_Archivnamen()
    {
        var zip = BuildZip("SuperMod.zip",
            ("ModInfo.xml", ModInfo),
            ("Config/x.xml", "<x/>"));

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeTrue(r.Message);
        File.Exists(Path.Combine(_installRoot, "Mods", "SuperMod", "ModInfo.xml")).Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "Mods", "SuperMod", "Config", "x.xml")).Should().BeTrue();
    }

    [Fact]
    public void Ohne_Mods_Ordner_und_ohne_ModInfo_schlaegt_es_fehl()
    {
        var zip = BuildZip("fremd.zip", ("irgendwas/datei.txt", "hi"));

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("kein 7DTD-Mod erkennbar");
    }

    /// <summary>Der Fall, an dem die alte Prüfung vorbeiging. Ein
    /// <b>absoluter</b> Eintragsname enthält kein <c>..</c>, kam also durch,
    /// und <c>Path.Combine</c> verwarf dann das Zielverzeichnis. Am
    /// Cyberpunk-Installer, der dieselbe Prüfung trug, nachgewiesen: die
    /// Datei landete außerhalb des Spiels und der Install meldete Erfolg.</summary>
    [Fact]
    public void Absoluter_Eintragsname_bricht_nicht_aus()
    {
        var opfer = Path.Combine(_tmp, "ausserhalb.txt");
        var zip = BuildZip("boese.zip",
            ("Mods/MeinMod/ModInfo.xml", ModInfo),
            (opfer, "UEBERNOMMEN"));

        var r = _installer.Install(zip, _game);

        File.Exists(opfer).Should().BeFalse();
        r.Success.Should().BeFalse("ein Ausbruchsversuch bricht den Install ab");
        r.Message.Should().Contain("herausschreiben");
    }

    /// <summary>Der ModInfo-Zweig trug die zweite Kopie des alten Schutzes.
    /// Hier braucht es den Fall mit <c>ModInfo.xml</c> im
    /// Wurzelverzeichnis: nur dann wird kein Präfix gesetzt und jeder
    /// Eintrag erreicht den Schutz.</summary>
    [Fact]
    public void Absoluter_Eintragsname_bricht_auch_im_ModInfo_Zweig_nicht_aus()
    {
        var opfer = Path.Combine(_tmp, "ausserhalb2.txt");
        var zip = BuildZip("boese2.zip",
            ("ModInfo.xml", ModInfo),
            (opfer, "UEBERNOMMEN"));

        var r = _installer.Install(zip, _game);

        File.Exists(opfer).Should().BeFalse();
        r.Success.Should().BeFalse();
        r.Message.Should().Contain("herausschreiben");
    }

    /// <summary>Liegt ein Mod-Ordner im Archiv, hält schon der
    /// <c>StripPrefix</c> den Fremdeintrag heraus — er gehört nicht unter
    /// das Präfix und wird gar nicht erst zum Auspacken angeboten. Der
    /// Install läuft damit <b>durch</b>, und das ist richtig: hier wurde
    /// nichts abgewehrt, sondern nur ausgewählt. Der Test steht hier, damit
    /// der Unterschied zum Zweig darüber festgehalten ist.</summary>
    [Fact]
    public void Unter_einem_Praefix_filtert_schon_die_Auswahl()
    {
        var opfer = Path.Combine(_tmp, "ausserhalb3.txt");
        var zip = BuildZip("mit-ordner.zip",
            ("MeinMod/ModInfo.xml", ModInfo),
            (opfer, "UEBERNOMMEN"));

        var r = _installer.Install(zip, _game);

        File.Exists(opfer).Should().BeFalse();
        r.Success.Should().BeTrue(r.Message);
        File.Exists(Path.Combine(_installRoot, "Mods", "MeinMod", "ModInfo.xml"))
            .Should().BeTrue();
    }

    [Fact]
    public void Kein_Archiv_wird_am_Inhalt_erkannt()
    {
        var kaputt = Path.Combine(_tmp, "abgebrochen.zip");
        File.WriteAllText(kaputt, "das ist kein ZIP");

        var r = _installer.Install(kaputt, _game);

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("kein lesbares Archiv");
    }

    [Fact]
    public void Endungs_Vorfilter_kommt_aus_dem_Baukasten()
    {
        _installer.SupportedExtensions.Should().BeEquivalentTo([".zip", ".rar", ".7z"]);
        _installer.HasSupportedExtension("mod.7Z").Should().BeTrue();
        _installer.HasSupportedExtension("liesmich.txt").Should().BeFalse();
    }
}
