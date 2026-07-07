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

    // Uploads base64 image as a temporary Drive file, makes it publicly readable.
    // Returns (fileId, publicUrl).
    public async Task<(string fileId, string url)> UploadTempImageAsync(string token, string base64DataUri)
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

        using var service = CreateService(token);
        var meta = new Google.Apis.Drive.v3.Data.File { Name = $"gtf_tmp_{Guid.NewGuid():N}" };
        using var stream = new MemoryStream(imageBytes);

        var upload = service.Files.Create(meta, stream, mimeType);
        upload.Fields = "id";
        await upload.UploadAsync();
        string fileId = upload.ResponseBody.Id;

        // Grant anyone reader access so Google Docs API can fetch the image URL
        var permission = new Permission { Type = "anyone", Role = "reader" };
        await service.Permissions.Create(permission, fileId).ExecuteAsync();

        string url = $"https://drive.google.com/uc?id={fileId}&export=download";
        return (fileId, url);
    }

    // Exports a Google Doc as PDF, saves it to folderId, deletes the original Doc.
    // Returns the new PDF file ID.
    public async Task<string> ExportAsPdfAsync(string token, string docId, string folderId, string fileName)
    {
        using var service = CreateService(token);

        // Files.Export in the C# client library does not support supportsAllDrives.
        // Use HttpClient directly to export from Shared Drive files.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var exportUri = $"https://www.googleapis.com/drive/v3/files/{docId}/export?mimeType=application%2Fpdf&supportsAllDrives=true";
        var response = await http.GetAsync(exportUri);
        response.EnsureSuccessStatusCode();
        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        using var pdfStream = new MemoryStream(pdfBytes);

        // Upload PDF to destination folder
        var meta = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".pdf",
            Parents = [folderId]
        };
        var upload = service.Files.Create(meta, pdfStream, "application/pdf");
        upload.Fields = "id";
        upload.SupportsAllDrives = true;
        await upload.UploadAsync();
        string pdfFileId = upload.ResponseBody.Id;

        // Move original Doc to trash (permanent delete often blocked by org policy)
        try
        {
            var trashMeta = new Google.Apis.Drive.v3.Data.File { Trashed = true };
            var trashRequest = service.Files.Update(trashMeta, docId);
            trashRequest.SupportsAllDrives = true;
            await trashRequest.ExecuteAsync();
        }
        catch
        {
            // Non-fatal: PDF was created; doc cleanup is best-effort
        }

        return pdfFileId;
    }

    // Downloads a Drive file as raw bytes (use for PDFs stored in Drive).
    public async Task<byte[]> DownloadFileAsync(string token, string fileId)
    {
        using var service = CreateService(token);
        var getRequest = service.Files.Get(fileId);
        getRequest.SupportsAllDrives = true;
        using var stream = new MemoryStream();
        await getRequest.DownloadAsync(stream);
        return stream.ToArray();
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
