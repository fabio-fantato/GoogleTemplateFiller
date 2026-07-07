using Google.Apis.Docs.v1.Data;

namespace GoogleTemplateFiller.services;

public static class PlaceholderReplacerService
{
    // Builds replaceAllText requests for simple key→value fields.
    // Placeholder format in template: {{fieldName}}
    public static List<Request> BuildFieldRequests(Dictionary<string, string> fields)
    {
        var requests = new List<Request>(fields.Count);
        foreach (var (key, value) in fields)
        {
            requests.Add(new Request
            {
                ReplaceAllText = new ReplaceAllTextRequest
                {
                    ContainsText = new SubstringMatchCriteria
                    {
                        Text = $"{{{{{key}}}}}",
                        MatchCase = true
                    },
                    ReplaceText = value ?? string.Empty
                }
            });
        }
        return requests;
    }

    // Builds replaceAllText requests for table placeholders.
    // Placeholder format: {{tableId_fieldName_row_N}}
    public static List<Request> BuildTableRequests(List<models.TableDefinition> tables)
    {
        var requests = new List<Request>();
        foreach (var table in tables)
        {
            for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
            {
                int rowNumber = rowIdx + 1;
                var rowValues = table.Rows[rowIdx];
                for (int colIdx = 0; colIdx < table.Fields.Count; colIdx++)
                {
                    string fieldName = table.Fields[colIdx];
                    string placeholder = $"{{{{{table.Id}_{fieldName}_row_{rowNumber}}}}}";
                    string value = colIdx < rowValues.Count ? rowValues[colIdx] ?? string.Empty : string.Empty;

                    requests.Add(new Request
                    {
                        ReplaceAllText = new ReplaceAllTextRequest
                        {
                            ContainsText = new SubstringMatchCriteria
                            {
                                Text = placeholder,
                                MatchCase = true
                            },
                            ReplaceText = value
                        }
                    });
                }
            }
        }
        return requests;
    }
}
