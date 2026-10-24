using System.IO.Compression;
using System.Security;
using System.Text;
using SixLabors.ImageSharp;

namespace API.Workers;

/// <summary>
/// Builds a minimal, valid Fixed-Layout EPUB3 (one full-page image per page, no reflowable text) from
/// an ordered set of already-processed JPEG streams - the layout an e-reader needs to render manga
/// pages as pages, instead of trying to reflow them like a text book.
/// </summary>
internal static class EpubWriter
{
    public static async Task WriteAsync(string outputPath, string title, string? author, string language,
        IReadOnlyList<Stream> pageImages, CancellationToken cancellationToken)
    {
        await using FileStream fs = File.Create(outputPath);
        using ZipArchive archive = new(fs, ZipArchiveMode.Create);

        // Per the EPUB spec, "mimetype" must be the first entry in the zip, stored uncompressed.
        ZipArchiveEntry mimetype = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        await using (Stream mimetypeStream = mimetype.Open())
        {
            byte[] bytes = Encoding.ASCII.GetBytes("application/epub+zip");
            await mimetypeStream.WriteAsync(bytes, cancellationToken);
        }

        await WriteTextEntryAsync(archive, "META-INF/container.xml", ContainerXml, cancellationToken);
        await WriteTextEntryAsync(archive, "OEBPS/style.css", StyleCss, cancellationToken);

        List<(string Id, int Width, int Height)> pages = new();
        for (int i = 0; i < pageImages.Count; i++)
        {
            string id = (i + 1).ToString("D4");
            Stream image = pageImages[i];
            image.Position = 0;
            ImageInfo info = await Image.IdentifyAsync(image, cancellationToken);
            pages.Add((id, info.Width, info.Height));

            image.Position = 0;
            ZipArchiveEntry imageEntry = archive.CreateEntry($"OEBPS/Images/{id}.jpg", CompressionLevel.Optimal);
            await using (Stream imageEntryStream = imageEntry.Open())
                await image.CopyToAsync(imageEntryStream, cancellationToken);

            await WriteTextEntryAsync(archive, $"OEBPS/Text/{id}.xhtml", PageXhtml(id, info.Width, info.Height, title), cancellationToken);
        }

        await WriteTextEntryAsync(archive, "OEBPS/nav.xhtml", NavXhtml(pages, title), cancellationToken);
        await WriteTextEntryAsync(archive, "OEBPS/content.opf", ContentOpf(pages, title, author, language), cancellationToken);
    }

    private static async Task WriteTextEntryAsync(ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using Stream entryStream = entry.Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        await entryStream.WriteAsync(bytes, cancellationToken);
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? value;

    private const string ContainerXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private const string StyleCss = """
        html, body { margin: 0; padding: 0; background-color: #000; }
        .page { text-align: center; }
        .page img { width: 100%; height: 100%; }
        """;

    private static string PageXhtml(string id, int width, int height, string title) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml">
        <head>
          <meta charset="UTF-8"/>
          <meta name="viewport" content="width={width}, height={height}"/>
          <link rel="stylesheet" type="text/css" href="../style.css"/>
          <title>{Escape(title)}</title>
        </head>
        <body>
          <div class="page"><img src="../Images/{id}.jpg" alt="Page {id}"/></div>
        </body>
        </html>
        """;

    private static string NavXhtml(List<(string Id, int Width, int Height)> pages, string title) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
        <head><title>{Escape(title)}</title></head>
        <body>
          <nav epub:type="toc" id="toc">
            <ol>
              <li><a href="Text/{pages[0].Id}.xhtml">{Escape(title)}</a></li>
            </ol>
          </nav>
        </body>
        </html>
        """;

    private static string ContentOpf(List<(string Id, int Width, int Height)> pages, string title, string? author, string language)
    {
        StringBuilder manifest = new();
        StringBuilder spine = new();
        foreach ((string id, _, _) in pages)
        {
            manifest.AppendLine($"""    <item id="img{id}" href="Images/{id}.jpg" media-type="image/jpeg"/>""");
            manifest.AppendLine($"""    <item id="page{id}" href="Text/{id}.xhtml" media-type="application/xhtml+xml"/>""");
            spine.AppendLine($"""    <itemref idref="page{id}"/>""");
        }

        string authorElement = author is null ? "" : $"<dc:creator>{Escape(author)}</dc:creator>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="BookId" xml:lang="{language}">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="BookId">urn:uuid:{Guid.NewGuid()}</dc:identifier>
                <dc:title>{Escape(title)}</dc:title>
                <dc:language>{language}</dc:language>
                {authorElement}
                <meta property="dcterms:modified">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>
                <meta property="rendition:layout">pre-paginated</meta>
                <meta property="rendition:orientation">portrait</meta>
                <meta property="rendition:spread">none</meta>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" properties="nav" media-type="application/xhtml+xml"/>
                <item id="css" href="style.css" media-type="text/css"/>
            {manifest}  </manifest>
              <spine>
            {spine}  </spine>
            </package>
            """;
    }
}
