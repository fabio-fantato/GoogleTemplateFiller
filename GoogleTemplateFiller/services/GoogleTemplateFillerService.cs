using GoogleTemplateFiller.models;

namespace GoogleTemplateFiller.services;

public class GoogleTemplateFillerService : IGoogleTemplateFillerService
{
    private readonly GoogleDriveService _driveService;
    private readonly GoogleDocsService _docsService;
    private readonly ConditionalReplacerService _conditionalReplacer;
    private readonly TableExpanderService _tableExpander;
    private readonly ImageReplacerService _imageReplacer;

    public GoogleTemplateFillerService(
        GoogleDriveService driveService,
        GoogleDocsService docsService,
        ConditionalReplacerService conditionalReplacer,
        TableExpanderService tableExpander,
        ImageReplacerService imageReplacer)
    {
        _driveService = driveService;
        _docsService = docsService;
        _conditionalReplacer = conditionalReplacer;
        _tableExpander = tableExpander;
        _imageReplacer = imageReplacer;
    }

    public async Task<(string documentId, string documentUrl)> FillTemplateAsync(string token, GoogleFillRequest request)
    {
        // 1. Copy template to destination folder
        string docId = await _driveService.CopyFileAsync(
            token, request.TemplateId, request.FolderId, request.FileName);

        // 2. Resolve {{if:name}}...{{endif:name}} blocks first, so a falsy
        //    block never leaves orphaned table/image/field placeholders behind
        await _conditionalReplacer.ReplaceAsync(token, docId, request.Fields);

        // 3. Expand table rows that exceed the template's single data row
        //    Must happen before text replacement so new placeholders exist in the doc
        await _tableExpander.ExpandAsync(token, docId, request.Tables);

        // 4. Replace image placeholders (delete text, insert inline image)
        //    Done before text replacement to avoid accidentally replacing partial matches
        await _imageReplacer.ReplaceAsync(token, request.FolderId, docId, request.Images);

        // 5. Replace all text placeholders in a single batch (fields + table cells)
        var replaceRequests = PlaceholderReplacerService.BuildFieldRequests(request.Fields);
        replaceRequests.AddRange(PlaceholderReplacerService.BuildTableRequests(request.Tables));
        await _docsService.BatchUpdateAsync(token, docId, replaceRequests);

        // No PDF export here: exporting is deferred to ExportFilledDocumentAsPdf, called
        // on demand at download time, so the fill call itself stays well under the
        // consumer's request timeout even for large templates.
        string url = $"https://docs.google.com/document/d/{docId}/edit";
        return (docId, url);
    }
}
