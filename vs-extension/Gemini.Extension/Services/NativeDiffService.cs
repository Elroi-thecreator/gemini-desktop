using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Gemini.Extension.Services
{
    public sealed class NativeDiffService
    {
        private readonly AsyncPackage package;

        public NativeDiffService(AsyncPackage package)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public async Task OpenSideBySideDiffAsync(string originalFilePath, string proposedContent, string title = "Gemini Proposed Changes")
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var diffService = await package.GetServiceAsync(typeof(SVsDifferenceService)) as IVsDifferenceService;
            if (diffService == null)
            {
                throw new InvalidOperationException("IVsDifferenceService is not available in Visual Studio.");
            }

            // Create a temporary file for the proposed version
            string ext = Path.GetExtension(originalFilePath);
            string tempProposedFile = Path.Combine(Path.GetTempPath(), $"Gemini_Proposed_{Guid.NewGuid():N}{ext}");
            File.WriteAllText(tempProposedFile, proposedContent);

            diffService.OpenComparisonWindow2(
                originalFilePath,
                tempProposedFile,
                title,
                $"{Path.GetFileName(originalFilePath)} Difference",
                $"{Path.GetFileName(originalFilePath)} (Current)",
                $"{Path.GetFileName(originalFilePath)} (Proposed)",
                $"{Path.GetFileName(originalFilePath)} (Inline)",
                null,
                0 // default flags
            );
        }
    }
}
