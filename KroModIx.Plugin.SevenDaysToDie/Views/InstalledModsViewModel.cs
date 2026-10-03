using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.SevenDaysToDie.Services;

namespace KroModIx.Plugin.SevenDaysToDie.Views;

/// <summary>Installiert-Tab. Zeigt 7DTD-Mod-Ordner unter
/// <c>&lt;InstallDir&gt;/Mods/</c>. Kein Bootstrap-Assistent — 7DTD hat
/// Vanilla-Mod-Loader (kein BepInEx/MelonLoader-Installation noetig).
/// Refresh laeuft off-thread (Kernprinzip 3). Rows tragen Cover/Author/
/// Version/Summary aus dem Nexus-Katalog via
/// <see cref="SevenDaysInstallManifestStore"/> + <see cref="SevenDaysNexusRowEnricher"/>.
/// Doppelklick + Details-Button oeffnen das gleiche <see cref="NexusModDetailWindow"/>
/// wie der Katalog-Tab.</summary>
public sealed partial class InstalledModsViewModel : ObservableObject, IDisposable
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
    private readonly EventHandler _installedHandler;
    private CancellationTokenSource _enrichCts = new();

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filterText = "";

    public ObservableCollection<ModRow> Rows { get; } = new();
    private List<ModRow> _allRows = new();

    public InstalledModsViewModel(DetectedGame game, SevenDaysModScanner scanner,
        SevenDaysInstallService installer, SevenDaysPathResolver paths,
        DownloadEventBus bus,
        SevenDaysInstallManifestStore manifests, INexusService nexus,
        CoverCache covers, SevenDaysNexusRowEnricher enricher, IHostServices host)
    {
        _game = game; _scanner = scanner; _installer = installer; _paths = paths;
        _bus = bus;
        _manifests = manifests; _nexus = nexus; _covers = covers; _enricher = enricher;
        _host = host;
        _installedHandler = (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        _bus.ModInstalled += _installedHandler;
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _bus.ModInstalled -= _installedHandler;
        try { _enrichCts.Cancel(); } catch { }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FilterText?.Trim() ?? "";
        Rows.Clear();
        var matched = string.IsNullOrEmpty(q)
            ? _allRows
            : _allRows.Where(r => r.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var r in matched) Rows.Add(r);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { _enrichCts.Cancel(); } catch { }
        _enrichCts = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            _paths.EnsureModsDir(_game);
            var mods = await Task.Run(() => _scanner.Scan(_game));
            _allRows = mods.Select(BuildRow).ToList();
            var enabled = mods.Count(m => m.IsEnabled);
            var disabled = mods.Count - enabled;
            StatusText = mods.Count == 0
                ? Strings.T("status.no_mods")
                : string.Format(Strings.T("status.mods_count"), mods.Count, enabled, disabled);
            ApplyFilter();

            _ = _enricher.EnrichBatchAsync(_allRows.ToList(), _enrichCts.Token);
        }
        finally { IsBusy = false; }
    }

    private ModRow BuildRow(SevenDaysMod mod)
    {
        var row = new ModRow(mod);
        var key = SevenDaysInstallManifestStore.BuildKey(mod.FolderName);
        var manifest = _manifests.TryGet(key);
        if (manifest is not null)
        {
            var modId = manifest.NexusModId;
            var version = manifest.NexusVersion;
            if (modId is null && !string.IsNullOrWhiteSpace(manifest.OriginalFilename))
            {
                modId = NexusFileNameParser.TryExtractModId(manifest.OriginalFilename);
                version ??= NexusFileNameParser.TryExtractVersion(manifest.OriginalFilename);
                if (modId is not null)
                {
                    _manifests.Save(key, new SevenDaysInstallManifest(
                        NexusModId: modId,
                        OriginalFilename: manifest.OriginalFilename,
                        NexusVersion: version,
                        InstalledAtUtc: manifest.InstalledAtUtc));
                }
            }
            row.NexusModId = modId;
            row.NexusVersion = version ?? "";
        }
        return row;
    }

    [RelayCommand]
    private void OpenPluginsFolder()
    {
        // v0.3.0: Ensure statt Get — nach einer Neuinstallation existiert der
        // Mods-Ordner nicht, und OpenDirectory auf einen fehlenden Pfad tut
        // nichts sichtbares. 7DTD legt ihn sonst erst beim ersten
        // Spielstart an.
        var dir = _paths.EnsureModsDir(_game);
        if (dir is null)
        {
            _host.Notifications.Notify(
                $"Mods-Ordner konnte nicht angelegt werden: {_game.InstallDir}",
                NotificationLevel.Warning);
            return;
        }
        _host.Shell.OpenDirectory(dir);
    }

    [RelayCommand]
    private void ShowDetail(ModRow? row)
    {
        if (row?.NexusModId is not int modId) return;
        SevenDaysNexusDetailLauncher.Show(modId, row.Cover, _nexus, _covers, _host);
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(ModRow? row)
    {
        if (row is null) return;
        try
        {
            IsBusy = true;
            var newPath = _installer.SetEnabled(row.Mod, !row.Mod.IsEnabled);
            row.Mod = row.Mod with { IsEnabled = !row.Mod.IsEnabled, Path = newPath };
            row.OnModChanged();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Toggle fehlgeschlagen: {Name}", row.Mod.DisplayName);
            await _host.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync(ModRow? row)
    {
        if (row is null) return;
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.uninstall_title"),
            string.Format(Strings.T("dialog.uninstall_msg"), row.Mod.DisplayName, row.Mod.Path),
            okLabel: Strings.T("dialog.uninstall_ok"));
        if (!ok) return;
        try
        {
            IsBusy = true;
            _installer.Uninstall(row.Mod);
            _manifests.Delete(SevenDaysInstallManifestStore.BuildKey(row.Mod.FolderName));
            _host.Notifications.Notify(Strings.T("notify.uninstalled_prefix") + row.Mod.DisplayName,
                NotificationLevel.Success);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Uninstall fehlgeschlagen: {Name}", row.Mod.DisplayName);
            await _host.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DisableAllAsync()
    {
        var targets = Rows.Where(r => r.Mod.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.no_enabled_mods"), NotificationLevel.Info);
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.disable_all_title"),
            string.Format(Strings.T("dialog.disable_all_msg"), targets.Count),
            okLabel: Strings.T("dialog.disable_all_ok"));
        if (!ok) return;
        int done = 0, failed = 0;
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.disable_bulk"), targets.Count));
        foreach (var row in targets)
        {
            scope.Report((double)(done + failed) / targets.Count,
                $"{done + failed + 1}/{targets.Count}: {row.Mod.DisplayName}");
            try { _installer.SetEnabled(row.Mod, false); done++; }
            catch (Exception ex) { _host.Logger.Warn(ex, "Bulk-Disable {Name}", row.Mod.DisplayName); failed++; }
        }
        _host.Notifications.Notify(string.Format(Strings.T("notify.bulk_disable_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task EnableAllAsync()
    {
        var targets = Rows.Where(r => !r.Mod.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.no_disabled_mods"), NotificationLevel.Info);
            return;
        }
        int done = 0, failed = 0;
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.enable_bulk"), targets.Count));
        foreach (var row in targets)
        {
            scope.Report((double)(done + failed) / targets.Count,
                $"{done + failed + 1}/{targets.Count}: {row.Mod.DisplayName}");
            try { _installer.SetEnabled(row.Mod, true); done++; }
            catch (Exception ex) { _host.Logger.Warn(ex, "Bulk-Enable {Name}", row.Mod.DisplayName); failed++; }
        }
        _host.Notifications.Notify(string.Format(Strings.T("notify.bulk_enable_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        await RefreshAsync();
    }
}

public sealed partial class ModRow : ObservableObject, ISevenDaysEnrichableRow
{
    public ModRow(SevenDaysMod mod) => Mod = mod;
    [ObservableProperty] private SevenDaysMod _mod;

    // ---- ISevenDaysEnrichableRow ----
    public int? NexusModId { get; set; }
    public bool IsEnriched { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    [NotifyPropertyChangedFor(nameof(NoCover))]
    private Bitmap? _cover;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _nexusName = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    private string _nexusAuthor = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    private string _nexusVersion = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string _nexusSummary = "";
    [ObservableProperty] private bool _hasNexusMatch;

    public bool HasCover => Cover is not null;
    public bool NoCover => Cover is null;
    public bool HasSummary => !string.IsNullOrWhiteSpace(NexusSummary);

    public string DisplayName => !string.IsNullOrWhiteSpace(NexusName) ? NexusName : Mod.DisplayName;

    public string StatusLabel => Mod.IsEnabled ? Strings.T("row.status_active") : Strings.T("row.status_inactive");
    public string ToggleButtonLabel => Mod.IsEnabled ? Strings.T("btn.disable") : Strings.T("btn.enable");
    public string TypeIcon => "📁";
    public string SizeText => Mod.SizeBytes switch
    {
        < 1024 => $"{Mod.SizeBytes} B",
        < 1024 * 1024 => $"{Mod.SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{Mod.SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{Mod.SizeBytes / (1024.0 * 1024 * 1024):F2} GB",
    };
    public string SubtitleText
    {
        get
        {
            var parts = new List<string>();
            var author = !string.IsNullOrWhiteSpace(NexusAuthor) ? NexusAuthor : Mod.Author;
            if (!string.IsNullOrWhiteSpace(author)) parts.Add(author!);
            var v = !string.IsNullOrWhiteSpace(NexusVersion) ? NexusVersion : (Mod.Version ?? "");
            v = v.Trim();
            if (v.Length > 0) parts.Add(char.IsDigit(v[0]) ? "v" + v : v);
            parts.Add(SizeText);
            parts.Add(Mod.InstalledUtc.ToLocalTime().ToString("yyyy-MM-dd"));
            return string.Join(" · ", parts);
        }
    }

    public void OnModChanged()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ToggleButtonLabel));
        OnPropertyChanged(nameof(SubtitleText));
    }
}
