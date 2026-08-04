using System.Text.Json;
using GoogleTemplateFiller.actions;
using GoogleTemplateFiller.models;

namespace GoogleTemplateFillerTest;

// ──────────────────────────────────────────────────────────────────────────────
// GETTING A GOOGLE ACCESS TOKEN FOR INTEGRATION TESTS
// ──────────────────────────────────────────────────────────────────────────────
// Integration tests read the token from the environment variable:
//   GOOGLE_ACCESS_TOKEN
//
// Quickest way to get one (requires gcloud CLI):
//   gcloud auth login
//   gcloud auth print-access-token
//
// Required OAuth scopes:
//   https://www.googleapis.com/auth/documents
//   https://www.googleapis.com/auth/drive
//
// Set before running:
//   export GOOGLE_ACCESS_TOKEN=$(gcloud auth print-access-token)
//   dotnet test
// ──────────────────────────────────────────────────────────────────────────────

public class UnitTestGoogleTemplateFiller
{
    // ── Unit tests (no API calls) ─────────────────────────────────────────────

    [Fact]
    public void ImagePlaceholder_Parse_FullFormat_ReturnsCorrectValues()
    {
        var result = ImagePlaceholder.Parse("{{img:companyLogo|w:200|h:100}}");

        Assert.NotNull(result);
        Assert.Equal("companyLogo", result.Name);
        Assert.Equal(200f, result.Width);
        Assert.Equal(100f, result.Height);
        Assert.Equal("{{img:companyLogo|w:200|h:100}}", result.RawPlaceholder);
    }

    [Fact]
    public void ImagePlaceholder_Parse_WidthOnly_HeightIsNull()
    {
        var result = ImagePlaceholder.Parse("{{img:logo|w:120}}");

        Assert.NotNull(result);
        Assert.Equal("logo", result.Name);
        Assert.Equal(120f, result.Width);
        Assert.Null(result.Height);
    }

    [Fact]
    public void ImagePlaceholder_Parse_NoSize_BothDimensionsNull()
    {
        var result = ImagePlaceholder.Parse("{{img:banner}}");

        Assert.NotNull(result);
        Assert.Equal("banner", result.Name);
        Assert.Null(result.Width);
        Assert.Null(result.Height);
    }

    [Fact]
    public void ImagePlaceholder_Parse_InvalidFormat_ReturnsNull()
    {
        Assert.Null(ImagePlaceholder.Parse("{{clientName}}"));
        Assert.Null(ImagePlaceholder.Parse("{{img:"));
        Assert.Null(ImagePlaceholder.Parse("not a placeholder"));
    }

    [Fact]
    public void PlaceholderReplacerService_BuildFieldRequests_CorrectPlaceholderFormat()
    {
        var fields = new Dictionary<string, string>
        {
            { "clientName", "Acme Corp" },
            { "total", "$100" }
        };

        var requests = GoogleTemplateFiller.services.PlaceholderReplacerService.BuildFieldRequests(fields);

        Assert.Equal(2, requests.Count);
        Assert.Equal("{{clientName}}", requests[0].ReplaceAllText.ContainsText.Text);
        Assert.Equal("Acme Corp", requests[0].ReplaceAllText.ReplaceText);
    }

    [Fact]
    public void PlaceholderReplacerService_BuildTableRequests_GeneratesCorrectPlaceholders()
    {
        var tables = new List<TableDefinition>
        {
            new()
            {
                Id = "orderItems",
                Fields = ["description", "qty"],
                Rows =
                [
                    ["Widget A", "2"],
                    ["Widget B", "5"]
                ]
            }
        };

        var requests = GoogleTemplateFiller.services.PlaceholderReplacerService.BuildTableRequests(tables);

        // 2 rows × 2 fields × 2 row-markers (row/linha) = 8 requests
        Assert.Equal(8, requests.Count);

        var texts = requests.Select(r => r.ReplaceAllText.ContainsText.Text).ToList();
        Assert.Contains("{{orderItems_description_row_1}}", texts);
        Assert.Contains("{{orderItems_qty_row_1}}", texts);
        Assert.Contains("{{orderItems_description_row_2}}", texts);
        Assert.Contains("{{orderItems_qty_row_2}}", texts);
        Assert.Contains("{{orderItems_description_linha_1}}", texts);
        Assert.Contains("{{orderItems_qty_linha_1}}", texts);
        Assert.Contains("{{orderItems_description_linha_2}}", texts);
        Assert.Contains("{{orderItems_qty_linha_2}}", texts);

        var row1Desc = requests.First(r => r.ReplaceAllText.ContainsText.Text == "{{orderItems_description_row_1}}");
        Assert.Equal("Widget A", row1Desc.ReplaceAllText.ReplaceText);
    }

    [Fact]
    public void GoogleFillRequest_Deserialization_Scenario1()
    {
        string json = LoadScenarioJson("scenario1_fields_only.json");
        var request = JsonSerializer.Deserialize<GoogleFillRequest>(json);

        Assert.NotNull(request);
        Assert.True(request.Fields.ContainsKey("clientName"));
        Assert.Equal("Acme Corp", request.Fields["clientName"]);
        Assert.Empty(request.Tables);
    }

    [Fact]
    public void GoogleFillRequest_Deserialization_Scenario2()
    {
        string json = LoadScenarioJson("scenario2_fields_and_table.json");
        var request = JsonSerializer.Deserialize<GoogleFillRequest>(json);

        Assert.NotNull(request);
        Assert.Single(request.Tables);
        Assert.Equal("orderItems", request.Tables[0].Id);
        Assert.Equal(4, request.Tables[0].Fields.Count);
        Assert.Equal(4, request.Tables[0].Rows.Count);
    }

    // ── Integration tests (require GOOGLE_ACCESS_TOKEN env var) ──────────────

    [Fact]
    public void Integration_Scenario1_FieldsOnly_FillsDocument()
    {
        string token = RequireToken();
        string templateId = RequireEnv("GOOGLE_TEMPLATE_ID");
        string folderId = RequireEnv("GOOGLE_FOLDER_ID");

        string json = LoadScenarioJson("scenario1_fields_only.json");

        var actions = new GoogleTemplateFillerActions();
        string docId = actions.FillGoogleDocTemplate(
            token, templateId, folderId, "Test_Scenario1_FieldsOnly", json,
            out string docUrl, out bool success, out string errorMessage);

        Assert.True(success, errorMessage);
        Assert.NotEmpty(docId);
        Assert.Contains("drive.google.com", docUrl);
    }

    [Fact]
    public void Integration_Scenario2_FieldsAndTable_ExpandsRows()
    {
        string token = RequireToken();
        string templateId = RequireEnv("GOOGLE_TEMPLATE_ID");
        string folderId = RequireEnv("GOOGLE_FOLDER_ID");

        string json = LoadScenarioJson("scenario2_fields_and_table.json");

        var actions = new GoogleTemplateFillerActions();
        string docId = actions.FillGoogleDocTemplate(
            token, templateId, folderId, "Test_Scenario2_WithTable", json,
            out string docUrl, out bool success, out string errorMessage);

        Assert.True(success, errorMessage);
        Assert.NotEmpty(docId);
    }

    [Fact]
    public void Integration_Scenario3_WithImage_ReplacesImagePlaceholder()
    {
        string token = RequireToken();
        string templateId = RequireEnv("GOOGLE_TEMPLATE_ID");
        string folderId = RequireEnv("GOOGLE_FOLDER_ID");

        // 1×1 transparent PNG in base64 as minimal test image
        const string tiny1x1Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var request = new
        {
            fields = new Dictionary<string, string> { { "clientName", "Acme Corp" } },
            images = new Dictionary<string, string> { { "companyLogo", tiny1x1Png } },
            tables = Array.Empty<object>()
        };

        var actions = new GoogleTemplateFillerActions();
        string docId = actions.FillGoogleDocTemplate(
            token, templateId, folderId, "Test_Scenario3_WithImage",
            JsonSerializer.Serialize(request),
            out string docUrl, out bool success, out string errorMessage);

        Assert.True(success, errorMessage);
        Assert.NotEmpty(docId);
    }

    [Fact]
    public void Integration_Scenario5_InspectTemplate_ReturnsPlaceholders()
    {
        string token = RequireToken();
        string templateId = RequireEnv("GOOGLE_TEMPLATE_ID");

        var actions = new GoogleTemplateFillerActions();
        string fieldsJson = actions.InspectTemplate(
            token, templateId,
            out string imagesJson, out string tablesJson, out string conditionsJson,
            out bool success, out string errorMessage);

        Assert.True(success, errorMessage);

        // Results must be valid JSON arrays
        var fields = JsonSerializer.Deserialize<List<string>>(fieldsJson);
        var images = JsonSerializer.Deserialize<List<JsonElement>>(imagesJson);
        var tables = JsonSerializer.Deserialize<List<JsonElement>>(tablesJson);
        var conditions = JsonSerializer.Deserialize<List<string>>(conditionsJson);

        Assert.NotNull(fields);
        Assert.NotNull(images);
        Assert.NotNull(tables);
        Assert.NotNull(conditions);

        // Log for manual verification
        Console.WriteLine($"Fields: {fieldsJson}");
        Console.WriteLine($"Images: {imagesJson}");
        Console.WriteLine($"Tables: {tablesJson}");
        Console.WriteLine($"Conditions: {conditionsJson}");
    }

    [Fact]
    public void Integration_Scenario4_FillAndDownloadPdf_ReturnsPdfBytes()
    {
        string token = RequireToken();
        string templateId = RequireEnv("GOOGLE_TEMPLATE_ID");
        string folderId = RequireEnv("GOOGLE_FOLDER_ID");

        string json = LoadScenarioJson("scenario1_fields_only.json");
        var actions = new GoogleTemplateFillerActions();

        // Fill template → get PDF file ID in Drive
        string pdfFileId = actions.FillGoogleDocTemplate(
            token, templateId, folderId, "Test_Scenario4_Download", json,
            out _, out bool fillSuccess, out string fillError);

        Assert.True(fillSuccess, fillError);
        Assert.NotEmpty(pdfFileId);

        // Download the PDF bytes
        byte[] pdfBytes = actions.DownloadPdfFromDrive(
            token, pdfFileId,
            out bool dlSuccess, out string dlError);

        Assert.True(dlSuccess, dlError);
        Assert.True(pdfBytes.Length > 0);
        // PDF magic bytes: %PDF
        Assert.Equal(0x25, pdfBytes[0]); // %
        Assert.Equal(0x50, pdfBytes[1]); // P
        Assert.Equal(0x44, pdfBytes[2]); // D
        Assert.Equal(0x46, pdfBytes[3]); // F
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string LoadScenarioJson(string fileName)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), "scenarios", fileName);
        if (File.Exists(path)) return File.ReadAllText(path);

        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        string? resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resource != null)
        {
            using var stream = asm.GetManifestResourceStream(resource)!;
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        }

        throw new FileNotFoundException($"Scenario file not found: {fileName}");
    }

    private static string RequireToken() => RequireEnv("GOOGLE_ACCESS_TOKEN");

    private static string RequireEnv(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Set env var '{name}' to run this integration test.");
        return value;
    }
}
