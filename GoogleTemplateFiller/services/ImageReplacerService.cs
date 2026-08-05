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

    // Finds all {{img:name|w:W|h:H}} placeholders (in the body, headers, and footers),
    // uploads images to Drive temporarily, deletes placeholder text, and inserts inline images.
    // Processes placeholders from highest to lowest document index to avoid index drift.
    public async Task ReplaceAsync(string token, string folderId, string documentId, Dictionary<string, string> images)
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
            tempFiles[name] = await _driveService.UploadTempImageAsync(token, folderId, images[name]);
        }

        try
        {
            // Body/header/footer segments each have their own index space, so ordering only
            // needs to prevent drift within the same segment; group and sort accordingly.
            var requests = new List<Request>();
            foreach (var entry in found
                .OrderBy(e => e.segmentId ?? "")
                .ThenByDescending(e => e.startIndex))
            {
                if (!tempFiles.TryGetValue(entry.placeholder.Name, out var tempFile)) continue;

                requests.Add(new Request
                {
                    DeleteContentRange = new DeleteContentRangeRequest
                    {
                        Range = new Google.Apis.Docs.v1.Data.Range
                        {
                            SegmentId = entry.segmentId,
                            StartIndex = entry.startIndex,
                            EndIndex = entry.endIndex
                        }
                    }
                });
                requests.Add(new Request
                {
                    InsertInlineImage = new InsertInlineImageRequest
                    {
                        Location = new Location { SegmentId = entry.segmentId, Index = entry.startIndex },
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

    private static List<(ImagePlaceholder placeholder, int startIndex, int endIndex, string? segmentId)> FindImagePlaceholders(Document doc)
    {
        var results = new List<(ImagePlaceholder, int, int, string?)>();
        ScanContent(doc.Body?.Content, results, segmentId: null);

        foreach (var header in doc.Headers?.Values ?? [])
            ScanContent(header.Content, results, header.HeaderId);

        foreach (var footer in doc.Footers?.Values ?? [])
            ScanContent(footer.Content, results, footer.FooterId);

        return results;
    }

    private static void ScanContent(
        IList<StructuralElement>? content,
        List<(ImagePlaceholder, int, int, string?)> results,
        string? segmentId)
    {
        foreach (var element in content ?? [])
        {
            if (element.Paragraph != null)
                ScanParagraph(element.Paragraph, results, segmentId);

            if (element.Table != null)
            {
                foreach (var row in element.Table.TableRows ?? [])
                foreach (var cell in row.TableCells ?? [])
                    ScanContent(cell.Content, results, segmentId);
            }
        }
    }

    // Scans the paragraph's full text (all runs concatenated), not run-by-run: Google Docs
    // frequently splits a pasted/duplicated placeholder across a new TextRun boundary even
    // when visually identical to the original, which would otherwise hide the tag from a
    // per-run scan (e.g. a duplicated {{img:name}} whose second copy silently fails to match).
    private static void ScanParagraph(
        Paragraph paragraph,
        List<(ImagePlaceholder, int, int, string?)> results,
        string? segmentId)
    {
        var runs = (paragraph.Elements ?? [])
            .Where(pe => !string.IsNullOrEmpty(pe.TextRun?.Content))
            .ToList();
        if (runs.Count == 0) return;

        var runStarts = new int[runs.Count];
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < runs.Count; i++)
        {
            runStarts[i] = sb.Length;
            sb.Append(runs[i].TextRun!.Content);
        }
        string text = sb.ToString();

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
                int docStart = LocalToDocIndex(imgStart, runs, runStarts);
                int docEnd = LocalToDocIndex(imgEnd + 2, runs, runStarts);
                results.Add((placeholder, docStart, docEnd, segmentId));
            }

            cursor = imgEnd + 2;
        }
    }

    private static int LocalToDocIndex(int localOffset, List<ParagraphElement> runs, int[] runStarts)
    {
        int i = runStarts.Length - 1;
        while (i > 0 && runStarts[i] > localOffset) i--;
        return runs[i].StartIndex!.Value + (localOffset - runStarts[i]);
    }
}
