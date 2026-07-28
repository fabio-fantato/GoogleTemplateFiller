using System.Text.RegularExpressions;
using Google.Apis.Docs.v1.Data;
using GoogleTemplateFiller.models;

namespace GoogleTemplateFiller.services;

public class TemplateInspectorService
{
    private readonly GoogleDocsService _docsService;

    public TemplateInspectorService(GoogleDocsService docsService)
    {
        _docsService = docsService;
    }

    public async Task<TemplateInspectionResult> InspectAsync(string token, string templateId)
    {
        var doc = await _docsService.GetDocumentAsync(token, templateId);
        string fullText = ExtractAllText(doc);
        return ParsePlaceholders(fullText);
    }

    private static TemplateInspectionResult ParsePlaceholders(string text)
    {
        var result = new TemplateInspectionResult();

        // Match everything inside {{ }}
        var matches = Regex.Matches(text, @"\{\{([^{}]+)\}\}");

        // tableId_field_row_1 → collect tables and their fields
        var tableFields = new Dictionary<string, SortedSet<string>>();
        // tableId_field_row_N (N>1) → ignore duplicates
        var tableRowPattern = new Regex(@"^(\w+)_(\w+)_row_(\d+)$");
        // img placeholder
        var imgPattern = new Regex(@"^img:([^|]+)(.*)$");
        // conditional block tags: if:name / endif:name
        var conditionPattern = new Regex(@"^(if|endif):(.+)$");

        foreach (Match m in matches)
        {
            string inner = m.Groups[1].Value.Trim();

            // Image placeholder: img:name|w:W|h:H
            var imgMatch = imgPattern.Match(inner);
            if (imgMatch.Success)
            {
                string raw = $"{{{{{inner}}}}}";
                var placeholder = ImagePlaceholder.Parse(raw);
                if (placeholder != null && !result.Images.Any(i => i.Name == placeholder.Name))
                    result.Images.Add(placeholder);
                continue;
            }

            // Conditional block tag: {{if:name}} / {{endif:name}}
            var conditionMatch = conditionPattern.Match(inner);
            if (conditionMatch.Success)
            {
                string name = conditionMatch.Groups[2].Value.Trim();
                if (!result.Conditions.Contains(name))
                    result.Conditions.Add(name);
                continue;
            }

            // Table placeholder: tableId_field_row_N
            var tableMatch = tableRowPattern.Match(inner);
            if (tableMatch.Success)
            {
                string tableId = tableMatch.Groups[1].Value;
                string field = tableMatch.Groups[2].Value;
                int rowNum = int.Parse(tableMatch.Groups[3].Value);

                // Only use row_1 to discover fields (avoids duplicates from expanded rows)
                if (rowNum == 1)
                {
                    if (!tableFields.ContainsKey(tableId))
                        tableFields[tableId] = new SortedSet<string>(StringComparer.Ordinal);
                    tableFields[tableId].Add(field);
                }
                continue;
            }

            // Plain field placeholder: {{fieldName}}
            if (!result.Fields.Contains(inner))
                result.Fields.Add(inner);
        }

        foreach (var (id, fields) in tableFields)
        {
            result.Tables.Add(new TableInspection
            {
                Id = id,
                Fields = fields.ToList()
            });
        }

        return result;
    }

    private static string ExtractAllText(Document doc)
    {
        var sb = new System.Text.StringBuilder();
        ExtractFromContent(doc.Body?.Content, sb);
        return sb.ToString();
    }

    private static void ExtractFromContent(IList<StructuralElement>? content, System.Text.StringBuilder sb)
    {
        foreach (var element in content ?? [])
        {
            if (element.Paragraph != null)
                foreach (var pe in element.Paragraph.Elements ?? [])
                    sb.Append(pe.TextRun?.Content ?? "");

            if (element.Table != null)
                foreach (var row in element.Table.TableRows ?? [])
                foreach (var cell in row.TableCells ?? [])
                    ExtractFromContent(cell.Content, sb);
        }
    }
}

public class TemplateInspectionResult
{
    public List<string> Fields { get; set; } = new();
    public List<ImagePlaceholder> Images { get; set; } = new();
    public List<TableInspection> Tables { get; set; } = new();
    public List<string> Conditions { get; set; } = new();
}

public class TableInspection
{
    public string Id { get; set; } = string.Empty;
    public List<string> Fields { get; set; } = new();
}
