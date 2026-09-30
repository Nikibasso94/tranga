using System.Diagnostics.CodeAnalysis;
using API.Schema.MangaContext;
using API.Workers.MangaDownloadWorkers;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.PeriodicWorkers;

/// <summary>
/// Creates Jobs to update available Chapters for all Manga that are marked for Download
/// </summary>
public class CheckForNewChaptersWorker(TimeSpan? interval = null, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    public DateTime LastExecution { get; set; } = DateTime.UnixEpoch;
    private TimeSpan? _fixedInterval = interval;
    // Read from Settings on every access (not captured once) so a change via the API takes effect on
    // the next reschedule, instead of only after a restart - unless explicitly overridden (e.g. tests).
    public TimeSpan Interval
    {
        // Existing settings.json files written before this setting existed deserialize it as 0 (the
        // struct's own field initializer doesn't apply to a JSON property that's simply absent) - falls
        // back to the original default instead of that turning into a zero-wait, hammering tight loop
        // (or silently becoming a much more frequent 30-minute check nobody asked for).
        get => _fixedInterval ?? TimeSpan.FromMinutes(Tranga.Settings.CheckForNewChaptersIntervalMinutes > 0
            ? Tranga.Settings.CheckForNewChaptersIntervalMinutes
            : Constants.CheckForNewChaptersInterval.TotalMinutes);
        set => _fixedInterval = value;
    }
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Debug("Checking for new chapters...");
        List<MangaConnectorId<Manga>> connectorIdsManga = await MangaContext.MangaConnectorToManga
            .Include(id => id.Obj)
            .Where(id => id.UseForDownload)
            .ToListAsync(CancellationToken);
        Log.DebugFormat("Creating {0} update jobs...", connectorIdsManga.Count);

        List<BaseWorker> newWorkers = connectorIdsManga.Select(id => new RetrieveMangaChaptersFromMangaconnectorWorker(id, Tranga.Settings.DownloadLanguage))
            .ToList<BaseWorker>();

        return newWorkers.ToArray();
    }
}