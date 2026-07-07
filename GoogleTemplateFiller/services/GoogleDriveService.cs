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

    public async Task DeleteFileAsync(string token, string fileId)
    {
        using var service = CreateService(token);
        await service.Files.Delete(fileId).ExecuteAsync();
    }

    private static DriveService CreateService(string token) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token)
        });
}
