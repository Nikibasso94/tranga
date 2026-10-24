using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using API.Schema.MangaContext;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace API.Workers.MangaDownloadWorkers;

/// <summary>
/// Converts a downloaded Chapter's .cbz archive into a Kindle-friendly .epub (fixed-layout, one
/// grayscale page per image, downscaled to a Kindle Paperwhite's resolution), replacing the .cbz on
/// disk - this is destructive by design (the user explicitly wants the .cbz gone, not kept alongside).
/// </summary>
public class ConvertChapterToEpubWorker(Chapter chapter, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn)
{
    private readonly string _chapterId = chapter.Key;

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private MangaContext MangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope) =>
        MangaContext = GetContext<MangaContext>(serviceScope);

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        if (await MangaContext.Chapters
                .Include(c => c.ParentManga)
                .ThenInclude(m => m.Authors)
                .Include(c => c.ParentManga)
                .ThenInclude(m => m.Library)
                .FirstOrDefaultAsync(c => c.Key == _chapterId, CancellationToken) is not { } chap)
        {
            Log.Error("Could not get Chapter.");
            this.Fail();
            return [];
        }

        if (chap.FullArchiveFilePath is not { } archivePath || !File.Exists(archivePath))
        {
            Log.Warn($"No archive on disk for {chap} - skipping epub conversion.");
            this.Fail();
            return [];
        }

        if (Path.GetExtension(archivePath).Equals(".epub", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug($"{chap} is already an epub.");
            return [];
        }

        List<Stream> images = [];
        try
        {
            using (ZipArchive cbz = ZipFile.OpenRead(archivePath))
            {
                IEnumerable<ZipArchiveEntry> pages = cbz.Entries
                    .Where(e => Path.GetExtension(e.Name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")
                    .OrderBy(e => int.TryParse(Path.GetFileNameWithoutExtension(e.Name), out int n) ? n : int.MaxValue)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

                foreach (ZipArchiveEntry page in pages)
                {
                    await using Stream pageStream = page.Open();
                    MemoryStream buffer = new();
                    await pageStream.CopyToAsync(buffer, CancellationToken);
                    buffer.Position = 0;
                    images.Add(await ProcessForKindle(buffer));
                }
            }

            if (images.Count < Constants.MinPlausibleChapterPageCount)
            {
                Log.Warn($"No usable pages found in {archivePath} - leaving the .cbz in place.");
                this.Fail();
                return [];
            }

            string epubPath = Path.ChangeExtension(archivePath, ".epub");
            string tempPath = epubPath + ".tmp";
            // Not chap.ToString() - that includes the raw internal Key (Identifiable.ToString()),
            // meant for logging, not a title a reader would actually see on their Kindle.
            string title = chap.VolumeNumber is { } vol
                ? $"{chap.ParentManga.Name} - Vol.{vol} Ch.{chap.ChapterNumber}"
                : $"{chap.ParentManga.Name} - Ch.{chap.ChapterNumber}";
            if (chap.Title is { } chapterTitle)
                title += $" - {chapterTitle}";
            string? author = chap.ParentManga.Authors.FirstOrDefault()?.AuthorName;
            await EpubWriter.WriteAsync(tempPath, title, author, chap.ParentManga.OriginalLanguage ?? "en", images, CancellationToken);

            // Only remove the .cbz once the .epub has been fully written - if conversion throws
            // partway through, the original archive is still there to retry from.
            File.Delete(archivePath);
            File.Move(tempPath, epubPath, overwrite: true);

            chap.FileName = new FileInfo(epubPath).Name;
            if (await MangaContext.Sync(CancellationToken, GetType(), "Converted to epub") is { success: false } result)
                Log.Error($"Failed to save database changes: {result.exceptionMessage}");

            Log.Info($"Converted {chap} to epub.");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to convert {chap} to epub: {e.Message}", e);
            this.Fail();
        }
        finally
        {
            images.ForEach(i => i.Dispose());
        }

        return [];
    }

    private static async Task<Stream> ProcessForKindle(Stream imageStream)
    {
        imageStream.Position = 0;
        MemoryStream processed = new();
        using Image image = await Image.LoadAsync(imageStream);
        image.Mutate(i => i
            .Resize(new ResizeOptions { Size = Constants.KindleImageSize, Mode = ResizeMode.Max })
            .Grayscale());
        await image.SaveAsJpegAsync(processed, new JpegEncoder { Quality = 85 });
        processed.Position = 0;
        return processed;
    }

    public override string ToString() => $"{base.ToString()} {_chapterId}";
}
