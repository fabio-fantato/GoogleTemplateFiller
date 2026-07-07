using Google.Apis.Auth.OAuth2;
using Google.Apis.Docs.v1;
using Google.Apis.Docs.v1.Data;
using Google.Apis.Services;

namespace GoogleTemplateFiller.services;

public class GoogleDocsService
{
    public async Task<Document> GetDocumentAsync(string token, string documentId)
    {
        using var service = CreateService(token);
        return await service.Documents.Get(documentId).ExecuteAsync();
    }

    public async Task BatchUpdateAsync(string token, string documentId, IList<Request> requests)
    {
        if (requests.Count == 0) return;
        using var service = CreateService(token);
        var body = new BatchUpdateDocumentRequest { Requests = requests };
        await service.Documents.BatchUpdate(body, documentId).ExecuteAsync();
    }

    private static DocsService CreateService(string token) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token)
        });
}
