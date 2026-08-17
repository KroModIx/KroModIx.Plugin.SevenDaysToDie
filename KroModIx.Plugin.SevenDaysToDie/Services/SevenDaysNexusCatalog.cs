using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.SevenDaysToDie.Services;

/// <summary>Nexus-Katalog fuer 7 Days to Die — wrappt <see cref="INexusService.SearchModsAsync"/>
/// (Contracts v1.15+, oeffentliches GraphQL, kein API-Key noetig fuer Read).
/// Analog Cyberpunk/DSP/ScheduleI-Plugin. Pagination mit 40 pro Seite,
/// Sort + Search.</summary>
public sealed class SevenDaysNexusCatalog
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Nexus-Domain-Slug fuer 7 Days to Die. Konventionell
    /// <c>7daystodie</c> — der Slug ist auch in offiziellen NexusMods-URLs
    /// so verwendet (<c>nexusmods.com/7daystodie/mods/...</c>). Anonyme
    /// curl-Verifikation geht auf HTTP 403 (Anti-Bot), aber der Slug ist
    /// stable und in mehreren Kroste-Recherchen bestaetigt.</summary>
    public const string GameSlug = "7daystodie";
    public const int PageSize = 40;

    private readonly INexusService _nexus;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly List<NexusCatalogEntry> _entries = new();
    private DateTime _cacheAt;
    private int _totalCount;
    private NexusSort _currentSort = NexusSort.LatestUpdate;
    private string _currentQuery = "";

    public SevenDaysNexusCatalog(INexusService nexus) => _nexus = nexus;

    public IReadOnlyList<NexusCatalogEntry> Cached => _entries;
    public DateTime CachedAtUtc => _cacheAt;
    public int TotalCount => _totalCount;
    public NexusSort CurrentSort => _currentSort;
    public string CurrentQuery => _currentQuery;
    public bool HasMore => _entries.Count < _totalCount;

    public Task<int> LoadFirstPageAsync(NexusSort sort, string? query, CancellationToken ct = default)
        => LoadCoreAsync(reset: true, sort, query ?? "", ct);

    public Task<int> LoadNextPageAsync(CancellationToken ct = default)
        => LoadCoreAsync(reset: false, _currentSort, _currentQuery, ct);

    private async Task<int> LoadCoreAsync(bool reset, NexusSort sort, string query, CancellationToken ct)
    {
        // SearchModsAsync ist oeffentlich (GraphQL) — kein API-Key noetig.
        await _loadGate.WaitAsync(ct);
        try
        {
            if (reset)
            {
                _entries.Clear();
                _totalCount = 0;
                _currentSort = sort;
                _currentQuery = query;
            }
            var result = await _nexus.SearchModsAsync(
                GameSlug, offset: _entries.Count, count: PageSize,
                sort: _currentSort,
                searchQuery: string.IsNullOrWhiteSpace(_currentQuery) ? null : _currentQuery,
                ct);
            _entries.AddRange(result.Entries);
            _totalCount = result.TotalCount;
            _cacheAt = DateTime.UtcNow;
            Log.Info("SevenDays-Katalog {Mode}: +{Added}, {N}/{Total} (sort={Sort} q='{Q}')",
                reset ? "reset" : "append", result.Entries.Count, _entries.Count, _totalCount,
                _currentSort, _currentQuery);
            return result.Entries.Count;
        }
        finally { _loadGate.Release(); }
    }
}
