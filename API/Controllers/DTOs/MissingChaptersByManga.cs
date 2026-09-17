namespace API.Controllers.DTOs;

public sealed record MissingChaptersByManga(string MangaId, string MangaName, int MissingCount);
