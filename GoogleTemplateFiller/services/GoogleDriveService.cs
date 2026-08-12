using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;

namespace GoogleTemplateFiller.services;

public class GoogleDriveService
{
    public async Task<string> CopyFileAsync(string token, string fileId, string folderId, string name)
    {
        using var service = CreateService(token);
        var meta = new Google.Apis.Drive.v3.Data.File { Name = name, Parents = [folderId] };
        var request = service.Files.Copy(meta, fileId);
        request.Fields = "id";
        request.SupportsAllDrives = true;
        var result = await request.ExecuteAsync();
        return result.Id;
    }

    // Uploads base64 image as a temporary Drive file inside folderId, makes it publicly readable.
    // Returns (fileId, publicUrl).
    // folderId must live on a Shared Drive: a Service Account has no storage quota of its own,
    // so a file created with no parent (or a parent on the account's own My Drive) fails with
    // "Service Accounts do not have storage quota" (HTTP 403).
    public async Task<(string fileId, string url)> UploadTempImageAsync(string token, string folderId, string base64DataUri)
    {
        string mimeType = "image/png";
        string base64 = base64DataUri;

        if (base64DataUri.StartsWith("data:", StringComparison.Ordinal))
        {
            int semicolon = base64DataUri.IndexOf(';');
            if (semicolon > 5) mimeType = base64DataUri[5..semicolon];
            int comma = base64DataUri.IndexOf(',');
            if (comma >= 0) base64 = base64DataUri[(comma + 1)..];
        }

        byte[] imageBytes = Convert.FromBase64String(base64);

        // The declared mimeType (from the data-URI prefix, or the "image/png"
        // default assumed for prefix-less values) is often wrong — sniff the
        // real format from the file's magic bytes and prefer that.
        mimeType = ImageMimeSniffer.Sniff(imageBytes) ?? mimeType;

        using var service = CreateService(token);
        var meta = new Google.Apis.Drive.v3.Data.File { Name = $"gtf_tmp_{Guid.NewGuid():N}", Parents = [folderId] };
        using var stream = new MemoryStream(imageBytes);

        var upload = service.Files.Create(meta, stream, mimeType);
        upload.Fields = "id";
        upload.SupportsAllDrives = true;
        var progress = await upload.UploadAsync();
        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || upload.ResponseBody == null)
            throw new InvalidOperationException(
                $"Image upload to Drive did not complete (status: {progress.Status}).", progress.Exception);
        string fileId = upload.ResponseBody.Id;

        // Grant anyone reader access so Google Docs API can fetch the image URL
        var permission = new Permission { Type = "anyone", Role = "reader" };
        var permissionRequest = service.Permissions.Create(permission, fileId);
        permissionRequest.SupportsAllDrives = true;
        await permissionRequest.ExecuteAsync();

        string url = $"https://drive.google.com/uc?id={fileId}&export=download";
        return (fileId, url);
    }

    // Exports a Google Doc directly to PDF bytes (no intermediate file persisted to Drive)
    // and deletes the source Doc. Used for on-demand "download as PDF" — skips the extra
    // upload-to-folder round trip that a persisted PDF copy would need.
    public async Task<byte[]> ExportDocAsPdfBytesAsync(string token, string docId)
    {
        byte[] pdfBytes = await ExportPdfBytesAsync(token, docId);
        await TrashFileAsync(token, docId);
        return pdfBytes;
    }

    // Exports a Google Doc directly to PDF bytes and keeps the source Doc untouched — unlike
    // ExportDocAsPdfBytesAsync, callable repeatedly against the same docId (e.g. from
    // DownloadFilledDocumentAsPdf, for a Doc left alive by FillTemplateWithCallback).
    public async Task<byte[]> ExportDocAsPdfBytesKeepSourceAsync(string token, string docId) =>
        await ExportPdfBytesAsync(token, docId);

    // Exports a Google Doc to PDF, persists that PDF as a new file inside targetFolderId,
    // and deletes the source Doc — unlike ExportDocAsPdfBytesAsync, the PDF itself survives
    // in Drive instead of only being returned as bytes.
    public async Task<(string fileId, string url, byte[] bytes)> ExportDocAsPdfToFolderAsync(
        string token, string docId, string targetFolderId)
    {
        byte[] pdfBytes = await ExportPdfBytesAsync(token, docId);

        using var service = CreateService(token);
        var docMeta = await service.Files.Get(docId).ExecuteAsync();
        string pdfName = $"{docMeta.Name}.pdf";

        var createMeta = new Google.Apis.Drive.v3.Data.File { Name = pdfName, Parents = [targetFolderId] };
        using var stream = new MemoryStream(pdfBytes);
        var upload = service.Files.Create(createMeta, stream, "application/pdf");
        upload.Fields = "id, webViewLink";
        upload.SupportsAllDrives = true;
        var progress = await upload.UploadAsync();
        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || upload.ResponseBody == null)
            throw new InvalidOperationException(
                $"PDF upload to Drive did not complete (status: {progress.Status}).", progress.Exception);

        string pdfFileId = upload.ResponseBody.Id;
        string pdfUrl = upload.ResponseBody.WebViewLink ?? $"https://drive.google.com/file/d/{pdfFileId}/view";

        await TrashFileAsync(token, docId);

        return (pdfFileId, pdfUrl, pdfBytes);
    }

    // Files.Export in the C# client library does not support supportsAllDrives.
    // Use HttpClient directly to export from Shared Drive files.
    private static async Task<byte[]> ExportPdfBytesAsync(string token, string docId)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var exportUri = $"https://www.googleapis.com/drive/v3/files/{docId}/export?mimeType=application%2Fpdf&supportsAllDrives=true";
        var response = await http.GetAsync(exportUri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    // Moves a file to trash (permanent delete often blocked by org policy). Best-effort.
    private async Task TrashFileAsync(string token, string fileId)
    {
        using var service = CreateService(token);
        try
        {
            var trashMeta = new Google.Apis.Drive.v3.Data.File { Trashed = true };
            var trashRequest = service.Files.Update(trashMeta, fileId);
            trashRequest.SupportsAllDrives = true;
            await trashRequest.ExecuteAsync();
        }
        catch
        {
            // Non-fatal: caller's primary result was already produced
        }
    }

    public async Task DeleteFileAsync(string token, string fileId)
    {
        using var service = CreateService(token);
        try
        {
            var request = service.Files.Delete(fileId);
            request.SupportsAllDrives = true;
            await request.ExecuteAsync();
        }
        catch
        {
            // Fallback: try trash if permanent delete is blocked
            try
            {
                var trashMeta = new Google.Apis.Drive.v3.Data.File { Trashed = true };
                var trashRequest = service.Files.Update(trashMeta, fileId);
                trashRequest.SupportsAllDrives = true;
                await trashRequest.ExecuteAsync();
            }
            catch { }
        }
    }

    private static DriveService CreateService(string token) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token)
        });
}
