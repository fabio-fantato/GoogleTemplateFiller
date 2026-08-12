using System.Text.Json.Serialization;

namespace GoogleTemplateFiller.models;

// Response shape of the requestUri callback used by FillTemplateWithCallback: the same
// OutSystems-shaped fill payload as OutSystemsFillRequest, except image values are file
// GUIDs (to resolve via downloadUri) instead of base64, plus the downloadUri/uploadUri
// to use for the rest of the flow.
public class CallbackFillPayload : OutSystemsFillRequest
{
    [JsonPropertyName("downloadUri")]
    public string DownloadUri { get; set; } = string.Empty;

    [JsonPropertyName("uploadUri")]
    public string UploadUri { get; set; } = string.Empty;

    // When true, the filled Doc is exported to PDF, the PDF is sent to uploadUri, and the
    // source Doc is deleted. When false, the Doc is filled but left untouched (no export, no
    // delete) — the caller gets documentId via uploadUri and fetches the PDF later, on demand,
    // via DownloadFilledDocumentAsPdf.
    [JsonPropertyName("uploadPDFWhenCompleted")]
    public bool UploadPdfWhenCompleted { get; set; }
}
