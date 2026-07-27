using System.Text.Json.Serialization;

namespace GoogleTemplateFiller.models;

// Mirrors the OutSystems JSON generator shape: a table object with a
// "columns" map (column1 -> field name) and "rows" as objects keyed the same way,
// instead of the ordered "fields"/"rows" arrays used by TableDefinition.
public class OutSystemsTableDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public Dictionary<string, string> Columns { get; set; } = new();

    [JsonPropertyName("rows")]
    public List<Dictionary<string, string>> Rows { get; set; } = new();

    // Converts to the internal TableDefinition shape, preserving column order
    // by sorting the "columnN" keys numerically (columns may be any count).
    public TableDefinition ToTableDefinition()
    {
        var orderedKeys = Columns.Keys
            .OrderBy(k => ExtractColumnIndex(k))
            .ToList();

        return new TableDefinition
        {
            Id = Id,
            Fields = orderedKeys.Select(k => Columns[k]).ToList(),
            Rows = Rows
                .Select(row => orderedKeys.Select(k => row.GetValueOrDefault(k, string.Empty)).ToList())
                .ToList()
        };
    }

    private static int ExtractColumnIndex(string columnKey)
    {
        var digits = new string(columnKey.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var index) ? index : int.MaxValue;
    }
}
