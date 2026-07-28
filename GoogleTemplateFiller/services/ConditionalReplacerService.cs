using Google.Apis.Docs.v1.Data;
using GoogleTemplateFiller.models;

namespace GoogleTemplateFiller.services;

public class ConditionalReplacerService
{
    private readonly GoogleDocsService _docsService;

    public ConditionalReplacerService(GoogleDocsService docsService)
    {
        _docsService = docsService;
    }

    // Resolves {{if:name}} ... {{endif:name}} blocks. If the field is truthy,
    // only the two tags are removed (content is kept, ready for later
    // placeholder passes). If falsy, tags and content are removed together.
    // Runs before table/image/text replacement so a false block never leaves
    // orphaned placeholders behind for those passes to trip over.
    public async Task ReplaceAsync(string token, string documentId, Dictionary<string, string> fields)
    {
        var doc = await _docsService.GetDocumentAsync(token, documentId);
        var tags = FindTags(doc);
        if (tags.Count == 0) return;

        var blocks = MatchBlocks(tags);
        if (blocks.Count == 0) return;

        // Process from highest index to lowest to avoid index drift.
        var requests = new List<Request>();
        foreach (var block in blocks.OrderByDescending(b => b.BlockStart))
        {
            bool truthy = ConditionalBlock.IsTruthy(fields, block.Name);

            if (truthy)
            {
                // Remove end tag first (higher index), then start tag.
                requests.Add(DeleteRange(block.ContentEnd, block.BlockEnd));
                requests.Add(DeleteRange(block.BlockStart, block.ContentStart));
            }
            else
            {
                requests.Add(DeleteRange(block.BlockStart, block.BlockEnd));
            }
        }

        await _docsService.BatchUpdateAsync(token, documentId, requests);
    }

    private static Request DeleteRange(int start, int end) => new()
    {
        DeleteContentRange = new DeleteContentRangeRequest
        {
            Range = new Google.Apis.Docs.v1.Data.Range { StartIndex = start, EndIndex = end }
        }
    };

    // Pairs tags by document order using a stack, independent of name nesting depth.
    // An {{endif:name}} only closes the innermost open tag if its name matches;
    // otherwise it's ignored (malformed template, left untouched).
    private static List<ConditionalBlock> MatchBlocks(List<ConditionalTag> tags)
    {
        var blocks = new List<ConditionalBlock>();
        var stack = new Stack<ConditionalTag>();

        foreach (var tag in tags.OrderBy(t => t.StartIndex))
        {
            if (!tag.IsEnd)
            {
                stack.Push(tag);
                continue;
            }

            if (stack.Count == 0 || stack.Peek().Name != tag.Name)
                continue;

            var start = stack.Pop();
            blocks.Add(new ConditionalBlock
            {
                Name = tag.Name,
                BlockStart = start.StartIndex,
                ContentStart = start.EndIndex,
                ContentEnd = tag.StartIndex,
                BlockEnd = tag.EndIndex
            });
        }

        return blocks;
    }

    private static List<ConditionalTag> FindTags(Document doc)
    {
        var results = new List<ConditionalTag>();
        ScanContent(doc.Body?.Content, results);
        return results;
    }

    private static void ScanContent(IList<StructuralElement>? content, List<ConditionalTag> results)
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

    private static void ScanParagraph(Paragraph paragraph, List<ConditionalTag> results)
    {
        foreach (var pe in paragraph.Elements ?? [])
        {
            string? text = pe.TextRun?.Content;
            if (string.IsNullOrEmpty(text)) continue;

            int cursor = 0;
            while (cursor < text.Length)
            {
                int tagStart = text.IndexOf("{{", cursor, StringComparison.Ordinal);
                if (tagStart < 0) break;
                int tagEnd = text.IndexOf("}}", tagStart, StringComparison.Ordinal);
                if (tagEnd < 0) break;

                string raw = text[tagStart..(tagEnd + 2)];
                var tag = ConditionalTag.Parse(raw);
                if (tag != null)
                {
                    tag.StartIndex = pe.StartIndex!.Value + tagStart;
                    tag.EndIndex = pe.StartIndex.Value + tagEnd + 2;
                    results.Add(tag);
                }

                cursor = tagEnd + 2;
            }
        }
    }
}
