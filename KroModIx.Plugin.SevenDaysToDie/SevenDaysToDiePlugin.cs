using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.SevenDaysToDie.Services;
using KroModIx.Plugin.SevenDaysToDie.Views;

namespace KroModIx.Plugin.SevenDaysToDie;

/// <summary>KroModIx-Plugin für 7 Days to Die (The Fun Pimps).
/// Drei Tabs (Installiert / Nexus / Downloads). 7DTD hat Vanilla-Mod-Loader
/// (kein BepInEx/MelonLoader-Bootstrap noetig — nur der
/// <c>&lt;InstallDir&gt;/Mods/</c>-Ordner muss existieren, ab A20 sind
/// Mods per Default aktiv). Jeder Mod ist ein Ordner mit ModInfo.xml.
/// Enable/Disable per <c>.disabled</c>-Suffix am Ordner-Namen.
/// Nutzt Host-Contract <see cref="IHostServices.Nexus"/> fuer den Katalog
/// (Contracts v1.15+, oeffentliches GraphQL). SharpCompress fuer
/// ZIP/RAR/7z-Install.</summary>
public sealed class SevenDaysToDiePlugin : IGameModPlugin, IUpdateNotifier
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.sevendaystodie",
        DisplayName: "7 Days to Die Mod-Manager",
        Version: "0.1.1",
        Author: "Kroste",
        Description: "Mod-Verwaltung für 7 Days to Die (The Fun Pimps). " +
            "v0.1.1: Manifest-GC — verwaiste Install-Manifests (Mod-Ordner " +
            "manuell geloescht, Manifest blieb) werden vor dem Update-Check " +
            "garbage-collected. Kein Phantom-Update-Badge mehr auf der " +
            "Sidebar-Kachel. " +
            "v0.1.0: Drei Tabs (Installiert / Nexus-Katalog / Downloads). " +
            "Vanilla-Loader — keine BepInEx/MelonLoader-Installation noetig, " +
            "nur der Mods/-Ordner. Jeder Mod ein eigener Ordner mit " +
            "ModInfo.xml (A20+ Attribut-Format und aelteres Element-Format " +
            "werden geparst). Enable/Disable via .disabled-Ordner-Suffix. " +
            "Nexus-Voll-Katalog via GraphQL (Sort + Search + Kategorie-Filter), " +
            "Detail-Dialog mit Rich-HTML-Beschreibung + KI-Zusammenfassung, " +
            "SharpCompress-Auto-Layout-Install (Mods/-Root oder ModInfo.xml-" +
            "Erkennung), IUpdateNotifier mit InstallManifest-Store, Row-" +
            "Konsistenz in allen drei Tabs (Cover + Details + Doppelklick). " +
            "DE+EN.");

    public IReadOnlyList<GameTarget> Targets { get; } = new[]
    {
        new GameTarget(
            GameId: "7-days-to-die",
            DisplayName: "7 Days to Die",
            SteamAppId: 251570,
            AlternativeExecutableNames: new[] { "7DaysToDie.exe", "7DaysToDie.x86_64" },
            Platforms: Platforms.Both),
    };

    private IHostServices? _host;
    private SevenDaysPathResolver? _paths;
    private SevenDaysModScanner? _scanner;
    private SevenDaysInstallService? _installer;
    private SevenDaysPaths? _pluginPaths;
    private SevenDaysNexusCatalog? _catalog;
    private SevenDaysDownloader? _downloader;
    private SevenDaysZipInstaller? _zipInstaller;
    private SevenDaysInstallManifestStore? _manifests;
    private SevenDaysUpdateChecker? _updateChecker;
    private CoverCache? _covers;
    private DownloadEventBus? _bus;
    private SevenDaysNexusRowEnricher? _enricher;
    private IReadOnlyList<DetectedGame> _activatedGames = Array.Empty<DetectedGame>();

    public Task InitializeAsync(IHostServices host,
        IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        Strings.Init(host.Localization);
        _paths = new SevenDaysPathResolver();
        _scanner = new SevenDaysModScanner(_paths);
        _installer = new SevenDaysInstallService();
        _pluginPaths = new SevenDaysPaths(host);
        _catalog = new SevenDaysNexusCatalog(host.Nexus);
        _downloader = new SevenDaysDownloader(host.Nexus,
            host.CreateHttpClient("sevendaystodie-downloads"), _pluginPaths);
        _manifests = new SevenDaysInstallManifestStore(host);
        _zipInstaller = new SevenDaysZipInstaller(_manifests);
        _updateChecker = new SevenDaysUpdateChecker(_manifests, _catalog);
        _covers = new CoverCache(host.CreateHttpClient("sevendaystodie-covers"), host);
        _bus = new DownloadEventBus();
        _enricher = new SevenDaysNexusRowEnricher(host.Nexus, _covers, host);
        _activatedGames = activatedGames;

        // v0.1.1: Manifest-GC-Callback — der UpdateChecker fragt VOR jedem
        // Version-Compare welche Mod-Ordner physisch existieren und purged
        // Manifests fuer geloeschte Mods (kein Phantom-Update-Badge mehr).
        _updateChecker.InstalledKeysProvider = () =>
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _activatedGames)
            {
                try
                {
                    foreach (var mod in _scanner.Scan(g))
                        keys.Add(SevenDaysInstallManifestStore.BuildKey(mod.FolderName));
                }
                catch (Exception ex) { host.Logger.Debug(ex, "Scan fuer Manifest-GC fehlgeschlagen: {Dir}", g.InstallDir); }
            }
            return keys;
        };

        // Auto-Update-Check nach 15s Bootstrap-Delay + Re-Check auf jedes
        // ModInstalled-Event.
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); } catch { return; }
            try { await _updateChecker.CheckAsync(ct); }
            catch (Exception ex) { host.Logger.Debug(ex, "Auto-Update-Check fehlgeschlagen"); }
            try { await host.RequestUpdateBadgeRefreshAsync(); } catch { }
        }, ct);
        _bus.ModInstalled += (_, _) =>
        {
            _ = Task.Run(async () =>
            {
                try { await _updateChecker.CheckAsync(); } catch { }
                try { await host.RequestUpdateBadgeRefreshAsync(); } catch { }
            });
        };

        foreach (var game in activatedGames)
        {
            _paths.EnsureModsDir(game);
            host.Logger.Info("7DTD initialisiert: {Dir}", game.InstallDir);
        }
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (_host is null || _paths is null || _scanner is null || _installer is null
            || _pluginPaths is null || _catalog is null || _downloader is null
            || _zipInstaller is null || _covers is null || _bus is null
            || _manifests is null || _enricher is null)
            yield break;
        yield return new InstalledTab(game, _scanner, _installer, _paths, _bus,
            _manifests, _host.Nexus, _covers, _enricher, _host);
        yield return new NexusTab(_catalog, _covers, _host.Nexus, _downloader, _bus, _host);
        yield return new DownloadsTab(game, _pluginPaths, _zipInstaller, _bus,
            _host.Nexus, _covers, _enricher, _host);
    }

    public Task ShutdownAsync()
    {
        _host?.Logger.Info("7DTD shutdown");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken ct)
    {
        if (_updateChecker is null || _activatedGames.Count == 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());
        var count = _updateChecker.PendingCount;
        if (count <= 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());
        var summary = count == 1
            ? $"1 Mod-Update verfuegbar: {_updateChecker.Pending[0].InstalledName}"
            : $"{count} Mod-Updates verfuegbar";
        var infos = _activatedGames
            .Where(g => g.Target.SteamAppId is int)
            .Select(g => new GameUpdateInfo(g.Target.SteamAppId!.Value, count, summary))
            .ToList();
        return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(infos);
    }

    private sealed class InstalledTab : IGameTabContribution
    {
        private readonly DetectedGame _game;
        private readonly SevenDaysModScanner _scanner;
        private readonly SevenDaysInstallService _installer;
        private readonly SevenDaysPathResolver _paths;
        private readonly DownloadEventBus _bus;
        private readonly SevenDaysInstallManifestStore _manifests;
        private readonly INexusService _nexus;
        private readonly CoverCache _covers;
        private readonly SevenDaysNexusRowEnricher _enricher;
        private readonly IHostServices _host;

        public InstalledTab(DetectedGame game, SevenDaysModScanner scanner,
            SevenDaysInstallService installer, SevenDaysPathResolver paths,
            DownloadEventBus bus, SevenDaysInstallManifestStore manifests,
            INexusService nexus, CoverCache covers,
            SevenDaysNexusRowEnricher enricher, IHostServices host)
        { _game = game; _scanner = scanner; _installer = installer; _paths = paths;
          _bus = bus; _manifests = manifests;
          _nexus = nexus; _covers = covers; _enricher = enricher; _host = host; }

        public string Id => "installed";
        public string Label => Strings.T("tab.installed");
        public string Icon => "\U0001F9E9";
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView
            {
                DataContext = new InstalledModsViewModel(_game, _scanner, _installer, _paths,
                    _bus, _manifests, _nexus, _covers, _enricher, _host),
            };
    }

    private sealed class NexusTab : IGameTabContribution
    {
        private readonly SevenDaysNexusCatalog _catalog;
        private readonly CoverCache _covers;
        private readonly INexusService _nexus;
        private readonly SevenDaysDownloader _downloader;
        private readonly DownloadEventBus _bus;
        private readonly IHostServices _host;

        public NexusTab(SevenDaysNexusCatalog catalog, CoverCache covers, INexusService nexus,
            SevenDaysDownloader downloader, DownloadEventBus bus, IHostServices host)
        { _catalog = catalog; _covers = covers; _nexus = nexus; _downloader = downloader;
          _bus = bus; _host = host; }

        public string Id => "nexus";
        public string Label => Strings.T("tab.nexus");
        public string Icon => "\U0001F310";
        public int Order => 10;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new NexusView
            {
                DataContext = new NexusViewModel(_catalog, _covers, _nexus, _downloader, _bus, _host),
            };
    }

    private sealed class DownloadsTab : IGameTabContribution
    {
        private readonly DetectedGame _game;
        private readonly SevenDaysPaths _paths;
        private readonly SevenDaysZipInstaller _installer;
        private readonly DownloadEventBus _bus;
        private readonly INexusService _nexus;
        private readonly CoverCache _covers;
        private readonly SevenDaysNexusRowEnricher _enricher;
        private readonly IHostServices _host;

        public DownloadsTab(DetectedGame game, SevenDaysPaths paths, SevenDaysZipInstaller installer,
            DownloadEventBus bus, INexusService nexus, CoverCache covers,
            SevenDaysNexusRowEnricher enricher, IHostServices host)
        { _game = game; _paths = paths; _installer = installer; _bus = bus;
          _nexus = nexus; _covers = covers; _enricher = enricher; _host = host; }

        public string Id => "downloads";
        public string Label => Strings.T("tab.downloads");
        public string Icon => "\U0001F4E5";
        public int Order => 20;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new DownloadsView
            {
                DataContext = new DownloadsViewModel(_game, _paths, _installer, _bus,
                    _nexus, _covers, _enricher, _host),
            };
    }
}
