namespace API.Controllers.DTOs;

public sealed record EpubConversionByManga(string MangaId, string MangaName, int DownloadedChapters, int EpubChapters, bool AutoConvertToEpub);
