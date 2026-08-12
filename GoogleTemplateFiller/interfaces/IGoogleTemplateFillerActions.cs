using OutSystems.ExternalLibraries.SDK;

namespace GoogleTemplateFiller.interfaces;

[OSInterface(
    Name = "GoogleTemplateFiller",
    Description = "Copies a Google Docs template, fills placeholders with data, expands dynamic tables, and replaces image placeholders using the Google Docs and Drive APIs.",
    IconResourceName = "GoogleTemplateFiller.Logo.png")]
public interface IGoogleTemplateFillerActions
{
    [OSAction(
        Description = "Copies a Google Docs template to a destination folder and fills it with fields, tables, and images from a JSON payload. Returns the filled Google Doc's ID (not a PDF) — call ExportFilledDocumentAsPdf separately when the PDF is actually needed, to keep this call fast.",
        ReturnName = "ResultDocumentId",
        ReturnDescription = "ID of the created (filled) Google Doc.")]
    string FillGoogleDocTemplate(
        [OSParameter(Description = "Google OAuth2 access token with Docs and Drive scopes.")] string token,
        [OSParameter(Description = "ID of the Google Docs template to copy.")] string templateId,
        [OSParameter(Description = "ID of the destination Google Drive folder.")] string folderId,
        [OSParameter(Description = "Name for the new document.")] string fileName,
        [OSParameter(Description = "JSON payload. See documentation for schema.")] string requestJson,
        [OSParameter(Description = "URL of the resulting Google Doc.")] out string resultDocumentUrl,
        [OSParameter(Description = "True if the operation succeeded.")] out bool success,
        [OSParameter(Description = "Error details if the operation failed.")] out string errorMessage
    );

    [OSAction(
        Description = "Same as FillGoogleDocTemplate, but accepts the OutSystems-generated JSON shape: tables come as separate 'table1', 'table2', ... properties (or a single 'tables' array), each with a 'columns' map (column1, column2, ...) and 'rows' as objects keyed the same way. Returns the filled Google Doc's ID (not a PDF) — call ExportFilledDocumentAsPdf separately when the PDF is actually needed, to keep this call fast.",
        ReturnName = "ResultDocumentId",
        ReturnDescription = "ID of the created (filled) Google Doc.")]
    string FillGoogleDocTemplateOutSystems(
        [OSParameter(Description = "Google OAuth2 access token with Docs and Drive scopes.")] string token,
        [OSParameter(Description = "ID of the Google Docs template to copy.")] string templateId,
        [OSParameter(Description = "ID of the destination Google Drive folder.")] string folderId,
        [OSParameter(Description = "Name for the new document.")] string fileName,
        [OSParameter(Description = "OutSystems-shaped JSON payload (fields, images, table1/table2/... with columns+rows).")] string requestJson,
        [OSParameter(Description = "URL of the resulting Google Doc.")] out string resultDocumentUrl,
        [OSParameter(Description = "True if the operation succeeded.")] out bool success,
        [OSParameter(Description = "Error details if the operation failed.")] out string errorMessage
    );

    [OSAction(
        Description = "Inspects a Google Docs template and returns all placeholders found: text fields, image placeholders, and table definitions with their field names. Use this to validate your JSON payload before filling.",
        ReturnName = "FieldsJson",
        ReturnDescription = "JSON array of text field placeholder names found in the template.")]
    string InspectTemplate(
        [OSParameter(Description = "Google OAuth2 access token.")] string token,
        [OSParameter(Description = "ID of the Google Docs template to inspect.")] string templateId,
        [OSParameter(Description = "JSON array of image placeholder objects (name, width, height).")] out string imagesJson,
        [OSParameter(Description = "JSON array of table objects with id and fields array.")] out string tablesJson,
        [OSParameter(Description = "JSON array of conditional block names found as {{if:name}}...{{endif:name}}.")] out string conditionsJson,
        [OSParameter(Description = "True if the inspection succeeded.")] out bool success,
        [OSParameter(Description = "Error details if the inspection failed.")] out string errorMessage
    );

    [OSAction(
        Description = "Exports a Google Doc (typically one returned by FillGoogleDocTemplate/FillGoogleDocTemplateOutSystems) directly to PDF bytes and deletes the source Doc. Call this on demand, only when the PDF is actually needed, so the fill call itself doesn't pay for a PDF export it might not need.",
        ReturnName = "PdfBytes",
        ReturnDescription = "Raw PDF file bytes ready to be served or stored.")]
    byte[] ExportFilledDocumentAsPdf(
        [OSParameter(Description = "Google OAuth2 access token with Docs and Drive scopes.")] string token,
        [OSParameter(Description = "ID of the filled Google Doc to export and delete.")] string documentId,
        [OSParameter(Description = "True if the export succeeded.")] out bool success,
        [OSParameter(Description = "Error details if the export failed.")] out string errorMessage
    );

    [OSAction(
        Description = "Exports a filled Google Doc (typically one returned by FillGoogleDocTemplate/FillGoogleDocTemplateOutSystems) to PDF, saves that PDF as a new file inside targetFolderId, and deletes the source Doc. Unlike ExportFilledDocumentAsPdf, the exported PDF is persisted in Drive (not only returned as bytes) and the source Doc is removed instead of kept.",
        ReturnName = "PdfBytes",
        ReturnDescription = "Raw PDF file bytes of the same file that was saved to targetFolderId.")]
    byte[] ExportAndPreserveFilledDocumentAsPdf(
        [OSParameter(Description = "Google OAuth2 access token with Docs and Drive scopes.")] string token,
        [OSParameter(Description = "ID of the filled Google Doc to export and delete.")] string documentId,
        [OSParameter(Description = "ID of the Google Drive folder where the exported PDF will be saved.")] string targetFolderId,
        [OSParameter(Description = "ID of the PDF file created in targetFolderId.")] out string resultPdfFileId,
        [OSParameter(Description = "URL of the PDF file created in targetFolderId.")] out string resultPdfUrl,
        [OSParameter(Description = "True if the export succeeded.")] out bool success,
        [OSParameter(Description = "Error details if the export failed.")] out string errorMessage
    );
}
