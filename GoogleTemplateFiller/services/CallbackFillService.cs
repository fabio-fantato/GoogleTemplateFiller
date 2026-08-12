using System.Net.Http.Headers;
using System.Text.Json;
using GoogleTemplateFiller.models;

namespace GoogleTemplateFiller.services;

// Orchestrates FillTemplateWithCallback: fetch the fill payload + image GUIDs from
// requestUri, resolve each image GUID to bytes via downloadUri, fill the template the
// same way FillGoogleDocTemplateOutSystems does, then either export+upload the PDF or
// leave the filled Doc alone for later on-demand download, and always report the outcome
// to uploadUri. requestUri/downloadUri/uploadUri all authenticate with the same Google
// token, sent as the X-API-KEY header (a caller-side convention, not a Google API header —
// Google Docs/Drive calls still use it as a Bearer token as usual).
public class CallbackFillService
{
    private readonly GoogleTemplateFillerService _fillerService;
    private readonly GoogleDriveService _driveService;
    private readonly HttpClient _http;

    public CallbackFillService(
        GoogleTemplateFillerService fillerService,
        GoogleDriveService driveService,
        HttpClient? http = null)
    {
        _fillerService = fillerService;
        _driveService = driveService;
        _http = http ?? new HttpClient();
    }

    public async Task<(string fileName, byte[] pdfBytes, bool hasFile)> RunAsync(string token, string requestGuid, string requestUri)
    {
        CallbackFillPayload? payload = null;
        string documentId = string.Empty;

        try
        {
            payload = await FetchPayloadAsync(token, requestUri, requestGuid);

            var guidMap = OutSystemsFillRequest.FlattenImageMap(payload.Images);
            var base64Map = new Dictionary<string, string>();
            foreach (var (name, fileGuid) in guidMap)
            {
                byte[] imageBytes = await DownloadImageAsync(token, payload.DownloadUri, fileGuid);
                string mimeType = ImageMimeSniffer.Sniff(imageBytes) ?? "image/png";
                base64Map[name] = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
            }

            // Images already contain data-URI base64 now, so ToGoogleFillRequest's own
            // data-URI prefixing is a no-op — it just passes these values through.
            payload.Images = JsonSerializer.SerializeToElement(base64Map);
            var fillRequest = payload.ToGoogleFillRequest();

            var (docId, _) = await _fillerService.FillTemplateAsync(token, fillRequest);
            documentId = docId;

            string fileName = string.Empty;
            byte[] pdfBytes = Array.Empty<byte>();
            bool hasFile = false;

            if (payload.UploadPdfWhenCompleted)
            {
                pdfBytes = await _driveService.ExportDocAsPdfBytesAsync(token, docId);
                fileName = fillRequest.FileName;
                if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    fileName += ".pdf";
                hasFile = true;
            }

            await UploadResultAsync(token, payload.UploadUri, requestGuid, documentId,
                isSuccess: true, errorMessage: string.Empty, hasFile, fileName, pdfBytes);

            return (fileName, pdfBytes, hasFile);
        }
        catch (Exception ex)
        {
            string message = ex.InnerException != null ? $"{ex.Message} | {ex.InnerException.Message}" : ex.Message;

            if (!string.IsNullOrEmpty(payload?.UploadUri))
            {
                try
                {
                    await UploadResultAsync(token, payload.UploadUri, requestGuid, documentId,
                        isSuccess: false, errorMessage: message, hasFile: false, fileName: string.Empty, fileBytes: Array.Empty<byte>());
                }
                catch
                {
                    // Best-effort failure notification — the original exception is what
                    // actually gets surfaced to the OutSystems caller below.
                }
            }

            throw;
        }
    }

    private async Task<CallbackFillPayload> FetchPayloadAsync(string token, string requestUri, string requestGuid)
    {
        string url = AppendQuery(requestUri, "requestGuid", requestGuid);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-API-KEY", token);

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<CallbackFillPayload>(json)
            ?? throw new InvalidOperationException("requestUri returned an empty or invalid payload.");
    }

    private async Task<byte[]> DownloadImageAsync(string token, string downloadUri, string fileGuid)
    {
        string url = AppendQuery(downloadUri, "fileGuid", fileGuid);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-API-KEY", token);

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    // Sent as multipart/form-data (not JSON+base64) so an OutSystems receiver can bind
    // fileContent directly to a BinaryData input parameter — no manual base64 decode, and
    // no ~33% base64 size inflation on top of the PDF's real byte size. Always reports
    // documentId/isSuccess/errorMessage/hasFile so the receiver knows the outcome even
    // when there's no file to attach.
    private async Task UploadResultAsync(
        string token, string uploadUri, string requestGuid, string documentId,
        bool isSuccess, string errorMessage, bool hasFile, string fileName, byte[] fileBytes)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(requestGuid), "requestGuid" },
            { new StringContent(documentId), "documentId" },
            { new StringContent(DateTime.UtcNow.ToString("o")), "timestamp" },
            { new StringContent(isSuccess.ToString()), "isSuccess" },
            { new StringContent(errorMessage), "errorMessage" },
            { new StringContent(hasFile.ToString()), "hasFile" }
        };

        if (hasFile)
        {
            content.Add(new StringContent(fileName), "fileName");
            var filePart = new ByteArrayContent(fileBytes);
            filePart.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(filePart, "fileContent", fileName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri) { Content = content };
        request.Headers.Add("X-API-KEY", token);

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static string AppendQuery(string uri, string key, string value)
    {
        string separator = uri.Contains('?') ? "&" : "?";
        return $"{uri}{separator}{key}={Uri.EscapeDataString(value)}";
    }
}
