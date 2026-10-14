using System;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace Gemini.Extension.Services
{
    public sealed class IdeContextService
    {
        private readonly AsyncPackage package;
        private DTE2 dte;

        public IdeContextService(AsyncPackage package)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
        }

        private async Task<DTE2> GetDteAsync()
        {
            try
            {
                if (dte == null)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
                }
                return dte;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini] GetDteAsync error: {ex.Message}");
                return null;
            }
        }

        public async Task<ActiveContextInfo> GetActiveDocumentContextAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dteInstance = await GetDteAsync();
                if (dteInstance == null) return null;

                var activeDoc = dteInstance.ActiveDocument;
                if (activeDoc == null) return null;

                var textDoc = activeDoc.Object("TextDocument") as TextDocument;
                string selectedText = "";
                int startLine = 0;
                int endLine = 0;

                if (textDoc != null && textDoc.Selection != null)
                {
                    selectedText = textDoc.Selection.Text;
                    startLine = textDoc.Selection.TopLine;
                    endLine = textDoc.Selection.BottomLine;
                }

                string solDir = null;
                try
                {
                    if (dteInstance.Solution != null && !string.IsNullOrEmpty(dteInstance.Solution.FullName))
                    {
                        solDir = Path.GetDirectoryName(dteInstance.Solution.FullName);
                    }
                }
                catch { }

                return new ActiveContextInfo
                {
                    FilePath = activeDoc.FullName,
                    FileName = activeDoc.Name,
                    Language = activeDoc.Language,
                    SelectedText = selectedText,
                    StartLine = startLine,
                    EndLine = endLine,
                    SolutionDirectory = solDir
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini] GetActiveDocumentContextAsync error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetSolutionDirectoryAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dteInstance = await GetDteAsync();
                if (dteInstance?.Solution != null && !string.IsNullOrEmpty(dteInstance.Solution.FullName))
                {
                    return Path.GetDirectoryName(dteInstance.Solution.FullName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini] GetSolutionDirectoryAsync error: {ex.Message}");
            }
            return Environment.CurrentDirectory;
        }

        public async Task<System.Collections.Generic.List<WorkspaceFileEntryItem>> ListSolutionFilesAsync()
        {
            var list = new System.Collections.Generic.List<WorkspaceFileEntryItem>();
            try
            {
                string solDir = await GetSolutionDirectoryAsync();
                if (string.IsNullOrEmpty(solDir) || !Directory.Exists(solDir)) return list;

                var dirInfo = new DirectoryInfo(solDir);
                EnumerateDirectory(dirInfo, solDir, list, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini] ListSolutionFilesAsync error: {ex.Message}");
            }

            return list;
        }

        private void EnumerateDirectory(DirectoryInfo dir, string rootPath, System.Collections.Generic.List<WorkspaceFileEntryItem> list, int depth)
        {
            if (depth > 5 || list.Count > 500) return;

            string dirName = dir.Name;
            if (dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("packages", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                foreach (var file in dir.GetFiles())
                {
                    string rel = file.FullName.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
                    list.Add(new WorkspaceFileEntryItem
                    {
                        name = file.Name,
                        path = file.FullName,
                        is_dir = false,
                        size = file.Length,
                        relative_path = rel.Replace('\\', '/')
                    });
                }

                foreach (var subDir in dir.GetDirectories())
                {
                    string rel = subDir.FullName.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
                    list.Add(new WorkspaceFileEntryItem
                    {
                        name = subDir.Name,
                        path = subDir.FullName,
                        is_dir = true,
                        size = 0,
                        relative_path = rel.Replace('\\', '/')
                    });

                    EnumerateDirectory(subDir, rootPath, list, depth + 1);
                }
            }
            catch { }
        }
    }

    public class WorkspaceFileEntryItem
    {
        public string name { get; set; }
        public string path { get; set; }
        public bool is_dir { get; set; }
        public long size { get; set; }
        public string relative_path { get; set; }
    }

    public class ActiveContextInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Language { get; set; }
        public string SelectedText { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string SolutionDirectory { get; set; }
    }
}
