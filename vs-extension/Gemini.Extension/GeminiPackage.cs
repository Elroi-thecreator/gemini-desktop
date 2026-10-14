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
    [ProvideBindingPath]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
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
        public object PendingContextAction { get; set; }

        public Options.GeminiOptionsPage Options
        {
            get
            {
                try
                {
                    return (Options.GeminiOptionsPage)GetDialogPage(typeof(Options.GeminiOptionsPage));
                }
                catch
                {
                    return null;
                }
            }
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);
            Instance = this;

            // Switch to UI thread to register commands
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            try
            {
                await Commands.GeminiToolWindowCommand.InitializeAsync(this);
                await Commands.GeminiEditorCommands.InitializeAsync(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] Command registration error: {ex}");
            }

            // Initialize extension background services with independent safety guards
            try { ProcessSupervisor = new Services.ProcessSupervisor(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] ProcessSupervisor error: {ex}"); }
            try { AcpClient = new Services.AcpClient(ProcessSupervisor); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] AcpClient error: {ex}"); }
            try { IdeContextService = new Services.IdeContextService(this); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] IdeContextService error: {ex}"); }
            try { NativeDiffService = new Services.NativeDiffService(this); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] NativeDiffService error: {ex}"); }
            try { DiagnosticsService = new Services.DiagnosticsService(this); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] DiagnosticsService error: {ex}"); }
            try
            {
                StorageService = new Services.StorageService();
                await StorageService.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] StorageService error: {ex}");
            }
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
