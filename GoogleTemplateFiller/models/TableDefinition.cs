using System.Text.Json.Serialization;

namespace GoogleTemplateFiller.models;

public class TableDefinition
{
    // Identifier used in placeholder names: {{id_field_row_N}}
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    // Ordered list of field names matching column order in the template
    [JsonPropertyName("fields")]
    public List<string> Fields { get; set; } = new();

    // Each inner list is one row of values, aligned to Fields
    [JsonPropertyName("rows")]
    public List<List<string>> Rows { get; set; } = new();
}
