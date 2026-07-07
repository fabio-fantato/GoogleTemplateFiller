using GoogleTemplateFiller.models;

namespace GoogleTemplateFiller.services;

public interface IGoogleTemplateFillerService
{
    Task<(string documentId, string documentUrl)> FillTemplateAsync(string token, GoogleFillRequest request);
}
