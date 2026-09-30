using System.ComponentModel.DataAnnotations;
using API.MangaConnectors;
using Microsoft.EntityFrameworkCore;

namespace API.Schema.MangaContext;

[PrimaryKey("Key")]
public class MangaConnectorId<T> : Identifiable where T : Identifiable
{
    public T Obj = null!;
    [StringLength(64)] public string ObjId { get; internal set; }

    [StringLength(32)] public string MangaConnectorName { get; private set; }

    [StringLength(256)] public string IdOnConnectorSite { get; init; }
    [Url] [StringLength(512)] public string? WebsiteUrl { get; internal set; }
    public bool UseForDownload { get; internal set; }

    /// <summary>
    /// When a download of this Chapter was last attempted (successful or not). Used to keep a large
    /// backlog of missing Chapters moving - without this, a chronically-failing Chapter with a low
    /// sort order would be re-selected on every scheduling cycle forever, starving out every other
    /// missing Chapter behind it in the queue (see StartNewChapterDownloadsWorker.GetMissingChapters).
    /// </summary>
    public DateTime? LastDownloadAttempt { get; internal set; }

    public MangaConnectorId(T obj, string mangaConnectorName, string idOnConnectorSite, string? websiteUrl,
        bool useForDownload = false)
        : base(TokenGen.CreateToken(typeof(MangaConnectorId<T>), mangaConnectorName, idOnConnectorSite))
    {
        this.Obj = obj;
        this.ObjId = obj.Key;
        this.MangaConnectorName = mangaConnectorName;
        this.IdOnConnectorSite = idOnConnectorSite;
        this.WebsiteUrl = websiteUrl;
        this.UseForDownload = useForDownload;
    }

    public MangaConnectorId(T obj, MangaConnector mangaConnector, string idOnConnectorSite, string? websiteUrl, bool useForDownload = false)
        : this(obj, mangaConnector.Name, idOnConnectorSite, websiteUrl, useForDownload) { }

    /// <summary>
    /// EF CORE ONLY!!!
    /// </summary>
    public MangaConnectorId(string key, string objId, string mangaConnectorName, string idOnConnectorSite, bool useForDownload, string? websiteUrl)
        : base(key)
    {
        this.ObjId = objId;
        this.MangaConnectorName = mangaConnectorName;
        this.IdOnConnectorSite = idOnConnectorSite;
        this.WebsiteUrl = websiteUrl;
        this.UseForDownload = useForDownload;
    }

    public override string ToString() => $"{base.ToString()} {Obj}";
}