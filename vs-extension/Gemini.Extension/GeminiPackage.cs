using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Gemini.Extension
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(GeminiPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(UI.GeminiToolWindow), Style = VsDockStyle.Tabbed, MultiInstances = false)]
    [ProvideOptionPage(typeof(Options.GeminiOptionsPage), "Gemini", "General", 0, 0, true)]
    public sealed class GeminiPackage : AsyncPackage
    {
        public const string PackageGuidString = "a5b9e023-38cf-4821-b3b4-f06b90709b11";

        public static GeminiPackage Instance { get; private set; }

        public Services.ProcessSupervisor ProcessSupervisor { get; private set; }
        public Services.AcpClient AcpClient { get; private set; }
        public Services.IdeContextService IdeContextService { get; private set; }
        public Services.NativeDiffService NativeDiffService { get; private set; }
        public Services.DiagnosticsService DiagnosticsService { get; private set; }
        public Services.StorageService StorageService { get; private set; }
        public Bridge.WebView2Bridge Bridge { get; set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);
            Instance = this;

            // Switch to UI thread to register commands
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await Commands.GeminiToolWindowCommand.InitializeAsync(this);
            await Commands.GeminiEditorCommands.InitializeAsync(this);

            // Initialize extension background services
            ProcessSupervisor = new Services.ProcessSupervisor();
            StorageService = new Services.StorageService();
            await StorageService.InitializeAsync();

            IdeContextService = new Services.IdeContextService(this);
            NativeDiffService = new Services.NativeDiffService(this);
            DiagnosticsService = new Services.DiagnosticsService(this);
            AcpClient = new Services.AcpClient(ProcessSupervisor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ProcessSupervisor?.Dispose();
                AcpClient?.Dispose();
                StorageService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
