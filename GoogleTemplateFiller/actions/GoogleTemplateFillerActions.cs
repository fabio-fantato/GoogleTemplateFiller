using System.Text.Json;
using GoogleTemplateFiller.interfaces;
using GoogleTemplateFiller.models;
using GoogleTemplateFiller.services;

namespace GoogleTemplateFiller.actions;

public class GoogleTemplateFillerActions : IGoogleTemplateFillerActions
{
    public string FillGoogleDocTemplate(
        string token,
        string templateId,
        string folderId,
        string fileName,
        string requestJson,
        out string resultDocumentUrl,
        out bool success,
        out string errorMessage)
    {
        resultDocumentUrl = string.Empty;
        success = false;
        errorMessage = string.Empty;

        try
        {
            var request = JsonSerializer.Deserialize<GoogleFillRequest>(requestJson)
                ?? throw new ArgumentException("Invalid or empty request JSON.");

            request.TemplateId = templateId;
            request.FolderId = folderId;
            request.FileName = fileName;

            var service = new GoogleTemplateFillerService(
                new GoogleDriveService(),
                new GoogleDocsService(),
                new TableExpanderService(new GoogleDocsService()),
                new ImageReplacerService(new GoogleDriveService(), new GoogleDocsService()));

            var (docId, docUrl) = service.FillTemplateAsync(token, request).GetAwaiter().GetResult();

            resultDocumentUrl = docUrl;
            success = true;
            return docId;
        }
        catch (Exception ex)
        {
            errorMessage = ex.InnerException != null
                ? $"{ex.Message} | {ex.InnerException.Message}"
                : ex.Message;
            return string.Empty;
        }
    }
}
