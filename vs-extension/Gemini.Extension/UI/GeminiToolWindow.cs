using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace Gemini.Extension.UI
{
    [Guid("4a123bc4-5d6e-7f8a-9b0c-1d2e3f4a5b6c")]
    public class GeminiToolWindow : ToolWindowPane
    {
        public GeminiToolWindow() : base(null)
        {
            Caption = "Gemini Desktop";
            Content = new GeminiToolWindowControl();
        }
    }
}
