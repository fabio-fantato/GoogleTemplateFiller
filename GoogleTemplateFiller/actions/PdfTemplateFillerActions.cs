using GoogleTemplateFiller.interfaces;
using GoogleTemplateFiller.services;

namespace GoogleTemplateFiller.actions
{
    /// <summary>
    /// Implementation of the PDF template filler for OutSystems ODC.
    /// Delegates the actual PDF manipulation (JSON parsing, "{{key}}" text substitution, table
    /// rendering) to <see cref="GoogleTemplateFillerService"/>, and wraps it with the
    /// out-parameter success/error pattern used across this library's actions.
    /// </summary>
    public class GoogleTemplateFillerActions : IGoogleTemplateFillerActions
    {
        public void FillPdfTemplate(
            byte[] templatePdf,
            string fillDataJson,
            out byte[] resultFile,
            out bool success,
            out string errorMessage)
        {
            resultFile = Array.Empty<byte>();
            success = false;
            errorMessage = string.Empty;

            try
            {
                var service = new GoogleTemplateFillerService();
                resultFile = service.FillTemplate(templatePdf, fillDataJson);
                success = true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error filling PDF template: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                }
            }
        }
    }
}
