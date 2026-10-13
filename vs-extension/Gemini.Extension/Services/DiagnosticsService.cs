using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using DTE2 = EnvDTE80.DTE2;

namespace Gemini.Extension.Services
{
    public sealed class DiagnosticsService
    {
        private readonly AsyncPackage package;

        public DiagnosticsService(AsyncPackage package)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public async Task<List<DiagnosticItem>> GetActiveDiagnosticsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
            var list = new List<DiagnosticItem>();

            if (dte?.ToolWindows?.ErrorList?.ErrorItems == null)
            {
                return list;
            }

            var errorItems = dte.ToolWindows.ErrorList.ErrorItems;
            int count = Math.Min(errorItems.Count, 20); // Capture top 20 active errors/warnings

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    var item = errorItems.Item(i);
                    list.Add(new DiagnosticItem
                    {
                        Description = item.Description,
                        FileName = item.FileName,
                        Line = item.Line,
                        Column = item.Column,
                        Project = item.Project
                    });
                }
                catch
                {
                    // Ignore transient COM error list changes
                }
            }

            return list;
        }
    }

    public class DiagnosticItem
    {
        public string Description { get; set; }
        public string FileName { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Project { get; set; }
    }
}
