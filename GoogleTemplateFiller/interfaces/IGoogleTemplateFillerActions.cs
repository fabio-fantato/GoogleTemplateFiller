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
}
