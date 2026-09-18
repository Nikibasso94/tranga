using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using API.Schema.MangaContext;

namespace API.Controllers.DTOs;

/// <summary>
/// Shortened Version of <see cref="Manga"/>
/// </summary>
public record MinimalManga(string Key, string Name, string Description, MangaReleaseStatus ReleaseStatus, IEnumerable<MangaConnectorId<Manga>> MangaConnectorIds, int TotalChapters = 0, int DownloadedChapters = 0) : Identifiable(Key)
{
    /// <summary>
    /// Name of the Manga
    /// </summary>
    [Required]
    [Description("Name of the Manga")]
    public string Name { get; init; } = Name;
    
    /// <summary>
    /// Description of the Manga
    /// </summary>
    [Required]
    [Description("Description of the Manga")]
    public string Description { get; init; } = Description;
    
    /// <summary>
    /// ReleaseStatus of the Manga
    /// </summary>
    [Required]
    [Description("ReleaseStatus of the Manga")]
    public MangaReleaseStatus ReleaseStatus { get; init; } = ReleaseStatus;
    
    /// <summary>
    /// Ids of the Manga on MangaConnectors
    /// </summary>
    [Required]
    [Description("Ids of the Manga on MangaConnectors")]
    public IEnumerable<MangaConnectorId<Manga>> MangaConnectorIds { get; init; } = MangaConnectorIds;

    /// <summary>
    /// Total number of known Chapters for the Manga
    /// </summary>
    [Required]
    [Description("Total number of known Chapters for the Manga")]
    public int TotalChapters { get; init; } = TotalChapters;

    /// <summary>
    /// Number of Chapters of the Manga that have been downloaded
    /// </summary>
    [Required]
    [Description("Number of Chapters of the Manga that have been downloaded")]
    public int DownloadedChapters { get; init; } = DownloadedChapters;
}