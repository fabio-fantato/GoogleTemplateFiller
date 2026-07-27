using OutSystems.ExternalLibraries.SDK;

namespace GoogleTemplateFiller.interfaces;

[OSInterface(
    Name = "GoogleTemplateFiller",
    Description = "Copies a Google Docs template, fills placeholders with data, expands dynamic tables, and replaces image placeholders using the Google Docs and Drive APIs.",
    IconResourceName = "GoogleTemplateFiller.Logo.png")]
public interface IGoogleTemplateFillerActions
{
    [OSAction(
        Description = "Copies a Google Docs template to a destination folder and fills it with fields, tables, and images from a JSON payload. Returns the new document ID.",
        ReturnName = "ResultDocumentId",
        ReturnDescription = "ID of the created Google Doc.")]
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
        Description = "Same as FillGoogleDocTemplate, but accepts the OutSystems-generated JSON shape: tables come as separate 'table1', 'table2', ... properties, each with a 'columns' map (column1, column2, ...) and 'rows' as objects keyed the same way, instead of a single 'tables' array.",
        ReturnName = "ResultDocumentId",
        ReturnDescription = "ID of the created Google Doc.")]
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
        [OSParameter(Description = "True if the inspection succeeded.")] out bool success,
        [OSParameter(Description = "Error details if the inspection failed.")] out string errorMessage
    );

    [OSAction(
        Description = "Downloads a PDF file from Google Drive by its file ID and returns the raw bytes.",
        ReturnName = "PdfBytes",
        ReturnDescription = "Raw PDF file bytes ready to be served or stored.")]
    byte[] DownloadPdfFromDrive(
        [OSParameter(Description = "Google OAuth2 access token with Drive scope.")] string token,
        [OSParameter(Description = "ID of the PDF file in Google Drive.")] string fileId,
        [OSParameter(Description = "True if the download succeeded.")] out bool success,
        [OSParameter(Description = "Error details if the download failed.")] out string errorMessage
    );
}
