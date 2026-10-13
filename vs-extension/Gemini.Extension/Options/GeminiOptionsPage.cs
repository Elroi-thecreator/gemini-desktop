using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace Gemini.Extension.Options
{
    [Guid("c8a7b6c5-d4e3-2f1a-0b9c-8d7e6f5a4b3c")]
    [ComVisible(true)]
    public class GeminiOptionsPage : DialogPage
    {
        [Category("General")]
        [DisplayName("Gemini CLI Path")]
        [Description("Absolute path to the gemini executable (e.g. C:\\Users\\...\\AppData\\Roaming\\npm\\gemini.cmd). Leave blank to auto-detect from PATH.")]
        public string GeminiCliPath { get; set; } = "";

        [Category("Model")]
        [DisplayName("Default Model")]
        [Description("Default model to route prompts to (e.g. gemini-3.8-flash, gemini-3.5-flash, gemini-3.1-pro-preview).")]
        public string DefaultModel { get; set; } = "gemini-3.8-flash";

        [Category("Security & Tools")]
        [DisplayName("Approval Mode")]
        [Description("Tool execution authorization policy: default (Ask user), auto_edit (Auto-approve file edits, ask commands), yolo (Auto-approve everything).")]
        public string ApprovalMode { get; set; } = "default";
    }
}
