using System.Text.Json.Serialization;

namespace GoogleTemplateFiller.models;

public class GoogleFillRequest
{
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; set; } = new();

    // key = image name, value = base64 data URI (e.g. "data:image/png;base64,...")
    [JsonPropertyName("images")]
    public Dictionary<string, string> Images { get; set; } = new();

    [JsonPropertyName("tables")]
    public List<TableDefinition> Tables { get; set; } = new();
}
