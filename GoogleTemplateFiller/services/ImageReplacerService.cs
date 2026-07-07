using Google.Apis.Docs.v1.Data;
using GoogleTemplateFiller.models;

namespace GoogleTemplateFiller.services;

public class ImageReplacerService
{
    private readonly GoogleDriveService _driveService;
    private readonly GoogleDocsService _docsService;

    public ImageReplacerService(GoogleDriveService driveService, GoogleDocsService docsService)
    {
        _driveService = driveService;
        _docsService = docsService;
    }

    // Finds all {{img:name|w:W|h:H}} placeholders, uploads images to Drive temporarily,
    // deletes placeholder text, and inserts inline images.
    // Processes placeholders from highest to lowest document index to avoid index drift.
    public async Task ReplaceAsync(string token, string documentId, Dictionary<string, string> images)
    {
        if (images.Count == 0) return;

        var doc = await _docsService.GetDocumentAsync(token, documentId);
        var found = FindImagePlaceholders(doc);
        if (found.Count == 0) return;

        // Upload all referenced images once
        var tempFiles = new Dictionary<string, (string fileId, string url)>();
        foreach (var entry in found)
        {
            string name = entry.placeholder.Name;
            if (tempFiles.ContainsKey(name) || !images.ContainsKey(name)) continue;
            tempFiles[name] = await _driveService.UploadTempImageAsync(token, images[name]);
        }

        try
        {
            // Build requests ordered from highest to lowest index (prevents index drift)
            var requests = new List<Request>();
            foreach (var entry in found.OrderByDescending(e => e.startIndex))
            {
                if (!tempFiles.TryGetValue(entry.placeholder.Name, out var tempFile)) continue;

                requests.Add(new Request
                {
                    DeleteContentRange = new DeleteContentRangeRequest
                    {
                        Range = new Google.Apis.Docs.v1.Data.Range
                        {
                            StartIndex = entry.startIndex,
                            EndIndex = entry.endIndex
                        }
                    }
                });
                requests.Add(new Request
                {
                    InsertInlineImage = new InsertInlineImageRequest
                    {
                        Location = new Location { Index = entry.startIndex },
                        Uri = tempFile.url,
                        ObjectSize = BuildObjectSize(entry.placeholder)
                    }
                });
            }

            await _docsService.BatchUpdateAsync(token, documentId, requests);
        }
        finally
        {
            foreach (var (fileId, _) in tempFiles.Values)
            {
                try { await _driveService.DeleteFileAsync(token, fileId); } catch { }
            }
        }
    }

    private static Size? BuildObjectSize(ImagePlaceholder img)
    {
        if (img.Width == null && img.Height == null) return null;
        var size = new Size();
        if (img.Width != null)
            size.Width = new Dimension { Magnitude = img.Width.Value, Unit = "PT" };
        if (img.Height != null)
            size.Height = new Dimension { Magnitude = img.Height.Value, Unit = "PT" };
        return size;
    }

    private static List<(ImagePlaceholder placeholder, int startIndex, int endIndex)> FindImagePlaceholders(Document doc)
    {
        var results = new List<(ImagePlaceholder, int, int)>();
        ScanContent(doc.Body?.Content, results);
        return results;
    }

    private static void ScanContent(
        IList<StructuralElement>? content,
        List<(ImagePlaceholder, int, int)> results)
    {
        foreach (var element in content ?? [])
        {
            if (element.Paragraph != null)
                ScanParagraph(element.Paragraph, results);

            if (element.Table != null)
            {
                foreach (var row in element.Table.TableRows ?? [])
                foreach (var cell in row.TableCells ?? [])
                    ScanContent(cell.Content, results);
            }
        }
    }

    private static void ScanParagraph(
        Paragraph paragraph,
        List<(ImagePlaceholder, int, int)> results)
    {
        foreach (var pe in paragraph.Elements ?? [])
        {
            string? text = pe.TextRun?.Content;
            if (string.IsNullOrEmpty(text)) continue;

            int cursor = 0;
            while (cursor < text.Length)
            {
                int imgStart = text.IndexOf("{{img:", cursor, StringComparison.Ordinal);
                if (imgStart < 0) break;
                int imgEnd = text.IndexOf("}}", imgStart, StringComparison.Ordinal);
                if (imgEnd < 0) break;

                string raw = text[imgStart..(imgEnd + 2)];
                var placeholder = ImagePlaceholder.Parse(raw);
                if (placeholder != null)
                {
                    int docStart = pe.StartIndex!.Value + imgStart;
                    int docEnd = pe.StartIndex.Value + imgEnd + 2;
                    results.Add((placeholder, docStart, docEnd));
                }

                cursor = imgEnd + 2;
            }
        }
    }
}
