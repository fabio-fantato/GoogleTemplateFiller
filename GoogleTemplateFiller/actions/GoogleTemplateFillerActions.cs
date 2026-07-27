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

    public string FillGoogleDocTemplateOutSystems(
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
            var osRequest = JsonSerializer.Deserialize<OutSystemsFillRequest>(requestJson)
                ?? throw new ArgumentException("Invalid or empty request JSON.");

            var request = osRequest.ToGoogleFillRequest();
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

    public string InspectTemplate(
        string token,
        string templateId,
        out string imagesJson,
        out string tablesJson,
        out bool success,
        out string errorMessage)
    {
        imagesJson = "[]";
        tablesJson = "[]";
        success = false;
        errorMessage = string.Empty;

        try
        {
            var inspector = new TemplateInspectorService(new GoogleDocsService());
            var result = inspector.InspectAsync(token, templateId).GetAwaiter().GetResult();

            var options = new JsonSerializerOptions { WriteIndented = false };

            imagesJson = JsonSerializer.Serialize(result.Images.Select(i => new
            {
                name = i.Name,
                width = i.Width,
                height = i.Height,
                placeholder = i.RawPlaceholder
            }), options);

            tablesJson = JsonSerializer.Serialize(result.Tables.Select(t => new
            {
                id = t.Id,
                fields = t.Fields
            }), options);

            success = true;
            return JsonSerializer.Serialize(result.Fields, options);
        }
        catch (Exception ex)
        {
            errorMessage = ex.InnerException != null
                ? $"{ex.Message} | {ex.InnerException.Message}"
                : ex.Message;
            return "[]";
        }
    }

    public byte[] DownloadPdfFromDrive(
        string token,
        string fileId,
        out bool success,
        out string errorMessage)
    {
        success = false;
        errorMessage = string.Empty;

        try
        {
            var driveService = new GoogleDriveService();
            byte[] bytes = driveService.DownloadFileAsync(token, fileId).GetAwaiter().GetResult();
            success = true;
            return bytes;
        }
        catch (Exception ex)
        {
            errorMessage = ex.InnerException != null
                ? $"{ex.Message} | {ex.InnerException.Message}"
                : ex.Message;
            return Array.Empty<byte>();
        }
    }
}
