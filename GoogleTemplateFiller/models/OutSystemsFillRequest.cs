using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoogleTemplateFiller.models;

// Request shape produced by OutSystems JSON generators: fields/images like
// GoogleFillRequest, but tables come as separate top-level "table1", "table2", ...
// properties instead of a single "tables" array. The count and column width of
// each table is arbitrary, so tables are captured via the extension data bag
// and picked out by key pattern rather than named properties.
public class OutSystemsFillRequest
{
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; set; } = new();

    [JsonPropertyName("images")]
    public Dictionary<string, string> Images { get; set; } = new();

    // Optional: tables sent as a single "tables" array instead of "table1"/"table2"/... keys.
    [JsonPropertyName("tables")]
    public List<OutSystemsTableDefinition> Tables { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();

    public GoogleFillRequest ToGoogleFillRequest()
    {
        var tables = ExtensionData
            .Where(kvp => kvp.Key.StartsWith("table", StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Value.Deserialize<OutSystemsTableDefinition>())
            .Where(t => t is not null)
            .Select(t => t!.ToTableDefinition())
            .Concat(Tables.Select(t => t.ToTableDefinition()))
            .ToList();

        return new GoogleFillRequest
        {
            TemplateId = TemplateId,
            FolderId = FolderId,
            FileName = FileName,
            Fields = Fields,
            Images = Images,
            Tables = tables
        };
    }
}
