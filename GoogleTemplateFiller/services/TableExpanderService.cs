using System.Text;
using Google.Apis.Docs.v1.Data;
using GoogleTemplateFiller.models;

namespace GoogleTemplateFiller.services;

public class TableExpanderService
{
    // Row-marker words accepted in table placeholders: "row" or the
    // Portuguese variant "linha".
    private static readonly string[] RowMarkers = ["row", "linha"];

    private readonly GoogleDocsService _docsService;

    public TableExpanderService(GoogleDocsService docsService)
    {
        _docsService = docsService;
    }

    // For each table definition that has more than one data row:
    //   1. Finds the table in the doc by its row_1 placeholders
    //   2. Inserts (N-1) new rows below the template data row
    //   3. Re-reads the doc and fills new cells with row_N placeholders
    // Requires two API round-trips per table that needs expansion.
    public async Task ExpandAsync(string token, string documentId, List<TableDefinition> tables)
    {
        foreach (var table in tables)
        {
            if (table.Rows.Count <= 1) continue;

            int rowsToInsert = table.Rows.Count - 1;

            // Read doc to locate the table and its data row
            var doc = await _docsService.GetDocumentAsync(token, documentId);
            var (tableElement, dataRowIndex, rowMarker) = FindTableElement(doc, table.Id);
            if (tableElement == null) continue;

            int tableStartIndex = tableElement.StartIndex!.Value;

            // Insert N-1 rows below dataRowIndex, dataRowIndex+1, ..., dataRowIndex+N-2
            // (processed in order so each new row is below the previous new row)
            var insertRequests = new List<Request>(rowsToInsert);
            for (int i = 0; i < rowsToInsert; i++)
            {
                insertRequests.Add(new Request
                {
                    InsertTableRow = new InsertTableRowRequest
                    {
                        TableCellLocation = new TableCellLocation
                        {
                            TableStartLocation = new Location { Index = tableStartIndex },
                            RowIndex = dataRowIndex + i,
                            ColumnIndex = 0
                        },
                        InsertBelow = true
                    }
                });
            }
            await _docsService.BatchUpdateAsync(token, documentId, insertRequests);

            // Re-read to get fresh indices for the new empty rows
            doc = await _docsService.GetDocumentAsync(token, documentId);
            (tableElement, dataRowIndex, rowMarker) = FindTableElement(doc, table.Id);
            if (tableElement == null) continue;

            // Collect (index, placeholderText) pairs for all new cells.
            // Process from highest to lowest index so earlier insertions don't shift later indices.
            var textInserts = new List<(int index, string text)>();

            for (int rowOffset = rowsToInsert; rowOffset >= 1; rowOffset--)
            {
                int rowIdx = dataRowIndex + rowOffset;
                var row = tableElement.Table.TableRows[rowIdx];
                int rowNumber = rowOffset + 1;

                for (int colIdx = row.TableCells.Count - 1; colIdx >= 0; colIdx--)
                {
                    if (colIdx >= table.Fields.Count) continue;

                    string fieldName = table.Fields[colIdx];
                    string placeholder = $"{{{{{table.Id}_{fieldName}_{rowMarker}_{rowNumber}}}}}";

                    // Insert at the start of the first paragraph in the empty cell
                    int insertIndex = row.TableCells[colIdx].Content[0].StartIndex!.Value;
                    textInserts.Add((insertIndex, placeholder));
                }
            }

            textInserts.Sort(static (a, b) => b.index.CompareTo(a.index));

            var textRequests = textInserts
                .Select(static t => new Request
                {
                    InsertText = new InsertTextRequest
                    {
                        Location = new Location { Index = t.index },
                        Text = t.text
                    }
                })
                .ToList();

            await _docsService.BatchUpdateAsync(token, documentId, textRequests);
        }
    }

    // Returns the StructuralElement containing the table whose cells include
    // "{{tableId_*_row_1}}" (or "_linha_1") placeholders, the row index of that
    // data row, and which row-marker word the template actually uses.
    private static (StructuralElement? element, int dataRowIndex, string rowMarker) FindTableElement(Document doc, string tableId)
    {
        foreach (var element in doc.Body?.Content ?? [])
        {
            if (element.Table == null) continue;

            var rows = element.Table.TableRows;
            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                foreach (var cell in rows[rowIdx].TableCells ?? [])
                {
                    string cellText = GetCellText(cell);
                    if (!cellText.Contains($"{{{{{tableId}_", StringComparison.Ordinal)) continue;

                    foreach (string marker in RowMarkers)
                    {
                        if (cellText.Contains($"_{marker}_1}}}}", StringComparison.Ordinal))
                            return (element, rowIdx, marker);
                    }
                }
            }
        }
        return (null, -1, RowMarkers[0]);
    }

    private static string GetCellText(TableCell cell)
    {
        var sb = new StringBuilder();
        foreach (var content in cell.Content ?? [])
            foreach (var pe in content.Paragraph?.Elements ?? [])
                sb.Append(pe.TextRun?.Content ?? "");
        return sb.ToString();
    }
}
