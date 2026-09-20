using System.Diagnostics.CodeAnalysis;
using API.MangaConnectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.MangaDownloadWorkers;

/// <summary>
/// Retrieves the metadata of available chapters on the Mangaconnector
/// </summary>
/// <param name="mcId"></param>
/// <param name="language"></param>
/// <param name="dependsOn"></param>
public class RetrieveMangaChaptersFromMangaconnectorWorker(MangaConnectorId<Manga> mcId, string language, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn)
{
    private readonly string _mangaConnectorIdId = mcId.Key;

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private ActionsContext ActionsContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        MangaContext = GetContext<MangaContext>(serviceScope);
        ActionsContext = GetContext<ActionsContext>(serviceScope);
    }
    
    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.DebugFormat("Getting Chapters for MangaConnectorId {0}...", _mangaConnectorIdId);
        // Getting MangaConnector info
        if (await MangaContext.MangaConnectorToManga
                .Include(id => id.Obj)
                .ThenInclude(m => m.Chapters)
                .ThenInclude(ch => ch.MangaConnectorIds)
                .FirstOrDefaultAsync(c => c.Key == _mangaConnectorIdId, CancellationToken) is not { } mangaConnectorId)
        {
            Log.Error("Could not get MangaConnectorId.");
            return []; //TODO Exception?
        }
        if (!Tranga.TryGetMangaConnector(mangaConnectorId.MangaConnectorName, out MangaConnector? mangaConnector))
        {
            Log.Error("Could not get MangaConnector.");
            return []; //TODO Exception?
        }
        Log.DebugFormat("Getting Chapters for MangaConnectorId {0}...", mangaConnectorId);
        
        Manga manga = mangaConnectorId.Obj;
        
        // Retrieve available Chapters from Connector
        (Chapter chapter, MangaConnectorId<Chapter> chapterId)[] allChapters =
            mangaConnector.GetChapters(mangaConnectorId, language).DistinctBy(c => c.Item1.Key).ToArray();
        Log.DebugFormat("Got {0} chapters from connector.", allChapters.Length);
        
        // Filter for new Chapters
        List<(Chapter chapter, MangaConnectorId<Chapter> chapterId)> newChapters = allChapters.Where<(Chapter chapter, MangaConnectorId<Chapter> chapterId)>(ch =>
            manga.Chapters.All(c => c.Key != ch.chapter.Key)).ToList();
        Log.DebugFormat("Got {0} new chapters.", newChapters.Count);

        // Add Chapters to Manga
        manga.Chapters = manga.Chapters.Union(newChapters.Select(ch => ch.chapter)).ToList();
        
        // Filter for new ChapterIds
        List<MangaConnectorId<Chapter>> existingChapterIds = manga.Chapters.SelectMany(c => c.MangaConnectorIds).ToList();
        List<MangaConnectorId<Chapter>> newIds = allChapters.Select(ch => ch.chapterId)
            .Where(newCh => !existingChapterIds.Any(existing =>
                existing.MangaConnectorName == newCh.MangaConnectorName &&
                existing.IdOnConnectorSite == newCh.IdOnConnectorSite))
            .ToList();
        // Match tracked entities of Chapters
        foreach (MangaConnectorId<Chapter> newId in newIds)
            newId.Obj = manga.Chapters.First(ch => ch.Key == newId.ObjId);
        Log.DebugFormat("Got {0} new download-Ids.", newIds.Count);

        // Refresh stale WebsiteUrls. IdOnConnectorSite is path-only for some Connectors (e.g. Mangaworld),
        // so a domain migration on the source site (mangaworld.cx -> mangaworld.mx, etc.) keeps the same
        // id - it's never treated as a "new" ChapterId above, so the stored WebsiteUrl would otherwise
        // keep pointing at the old, now-dead domain forever.
        Dictionary<(string MangaConnectorName, string IdOnConnectorSite), string?> freshUrls = allChapters
            .Select(ch => ch.chapterId)
            .GroupBy(id => (id.MangaConnectorName, id.IdOnConnectorSite))
            .ToDictionary(g => g.Key, g => g.First().WebsiteUrl);
        foreach (MangaConnectorId<Chapter> existing in existingChapterIds)
        {
            if (freshUrls.TryGetValue((existing.MangaConnectorName, existing.IdOnConnectorSite), out string? freshUrl)
                && freshUrl is not null && freshUrl != existing.WebsiteUrl)
                existing.WebsiteUrl = freshUrl;
        }

        // Add new ChapterIds to Database
        MangaContext.MangaConnectorToChapter.AddRange(newIds);

        // If Manga is marked for Download from Connector, mark newly-discovered ChapterIds as UseForDownload.
        // This covers brand-new Chapters, but also existing, not-yet-downloaded Chapters that got a fresh
        // IdOnConnectorSite (e.g. a dead link the site re-issued under a new URL) - otherwise the stale
        // dead link (still UseForDownload) keeps being retried forever while the working one sits unused.
        // Already-downloaded Chapters are left alone; the sibling swap mirrors ChaptersController.MarkAsRequested.
        if (mangaConnectorId.UseForDownload)
        {
            Dictionary<string, List<MangaConnectorId<Chapter>>> idsByChapter = existingChapterIds.Concat(newIds)
                .GroupBy(id => id.ObjId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (MangaConnectorId<Chapter> newId in newIds)
            {
                if (newId.Obj.Downloaded)
                    continue;

                newId.UseForDownload = true;
                foreach (MangaConnectorId<Chapter> sibling in idsByChapter[newId.ObjId].Where(id => id.Key != newId.Key))
                    sibling.UseForDownload = false;
            }
        }

        if(await MangaContext.Sync(CancellationToken, GetType(), "Chapters retrieved") is { success: false } mangaContextException)
            Log.ErrorFormat("Failed to save database changes: {0}", mangaContextException.exceptionMessage);

        ActionsContext.Actions.Add(new ChaptersRetrievedActionRecord(manga));
        if(await ActionsContext.Sync(CancellationToken, GetType(), "Chapters retrieved") is { success: false } actionsContextException)
            Log.ErrorFormat("Failed to save database changes: {0}", actionsContextException.exceptionMessage);

        return [];
    }

    public override string ToString() => $"{base.ToString()} {_mangaConnectorIdId}";
}