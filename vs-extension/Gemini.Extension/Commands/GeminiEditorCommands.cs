using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Gemini.Extension.Commands
{
    internal sealed class GeminiEditorCommands
    {
        public const int AskSelectionCommandId = 0x0200;
        public const int ExplainCodeCommandId = 0x0201;
        public const int FixErrorsCommandId = 0x0202;

        public static readonly Guid CommandSet = new Guid("c94541cb-708a-4467-b501-c5ce32918452");

        private readonly AsyncPackage package;

        private GeminiEditorCommands(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            // 1. Ask Gemini about Selection
            var askCmdId = new CommandID(CommandSet, AskSelectionCommandId);
            commandService.AddCommand(new MenuCommand(ExecuteAskSelection, askCmdId));

            // 2. Explain Code
            var explainCmdId = new CommandID(CommandSet, ExplainCodeCommandId);
            commandService.AddCommand(new MenuCommand(ExecuteExplainCode, explainCmdId));

            // 3. Fix Errors
            var fixCmdId = new CommandID(CommandSet, FixErrorsCommandId);
            commandService.AddCommand(new MenuCommand(ExecuteFixErrors, fixCmdId));
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            new GeminiEditorCommands(package, commandService);
        }

        private async void ExecuteAskSelection(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowToolWindow();

            var pkg = GeminiPackage.Instance ?? (package as GeminiPackage);
            if (pkg == null || pkg.IdeContextService == null) return;

            var ctx = await pkg.IdeContextService.GetActiveDocumentContextAsync();
            pkg.Bridge?.DispatchEvent("vs-context-action", new
            {
                action = "selection_context",
                context = ctx
            });
        }

        private async void ExecuteExplainCode(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowToolWindow();

            var pkg = GeminiPackage.Instance ?? (package as GeminiPackage);
            if (pkg == null || pkg.IdeContextService == null) return;

            var ctx = await pkg.IdeContextService.GetActiveDocumentContextAsync();
            string prompt = string.IsNullOrWhiteSpace(ctx?.SelectedText)
                ? $"Please explain the code in {ctx?.FileName ?? "the current file"}."
                : $"Please explain the selected code in {ctx?.FileName} (lines {ctx?.StartLine}-{ctx?.EndLine}):\n\n```{ctx?.Language}\n{ctx?.SelectedText}\n```";

            pkg.Bridge?.DispatchEvent("vs-context-action", new
            {
                action = "auto_prompt",
                prompt = prompt,
                context = ctx
            });
        }

        private async void ExecuteFixErrors(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ShowToolWindow();

            var pkg = GeminiPackage.Instance ?? (package as GeminiPackage);
            if (pkg == null || pkg.DiagnosticsService == null) return;

            var diagnostics = await pkg.DiagnosticsService.GetActiveDiagnosticsAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Please help me fix the following compilation errors in my Visual Studio solution:\n");

            foreach (var diag in diagnostics)
            {
                sb.AppendLine($"- **{diag.FileName}** (line {diag.Line}, col {diag.Column}): {diag.Description}");
            }

            pkg.Bridge?.DispatchEvent("vs-context-action", new
            {
                action = "auto_prompt",
                prompt = sb.ToString(),
                diagnostics = diagnostics
            });
        }

        private void ShowToolWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = package.FindToolWindow(typeof(UI.GeminiToolWindow), 0, true);
            if (window?.Frame != null)
            {
                var windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }
        }
    }
}
