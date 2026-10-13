# 🚀 Gemini Desktop for Visual Studio 2022 (VSIX)

This directory contains the native **Visual Studio 2022 Extension (VSIX)** for Gemini Desktop, bringing autonomous AI pair programming, Agent Client Protocol (ACP) streaming, and Google Gemini models directly inside Visual Studio 2022 Community, Professional, and Enterprise.

---

## 🏗️ Architecture Overview

The extension adopts a high-performance **Hybrid ToolWindow Architecture**:
- **UI Presentation**: The existing Svelte 5 runes + Tailwind CSS v4 frontend is hosted directly inside a Visual Studio `ToolWindowPane` via `Microsoft.Web.WebView2.Wpf`.
- **Bridge & Transport**: `WebView2Bridge.cs` shims Tauri IPC (`window.__TAURI_INTERNALS__.invoke` / `listen`) and routes calls seamlessly to C# backend services via `postMessage`.
- **Process Supervision**: `ProcessSupervisor.cs` implements Win32 Job Objects (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) to ensure zero zombie `gemini` CLI or terminal child processes when Visual Studio exits or restarts.
- **Protocol Streaming**: `AcpClient.cs` communicates with the Gemini CLI via the Agent Client Protocol (ACP) using JSON-RPC 2.0 over standard I/O.
- **IDE Superpowers**:
  - `IdeContextService.cs`: Queries active document path, selected code lines, and solution structure via `EnvDTE80.DTE2`.
  - `NativeDiffService.cs`: Displays side-by-side Visual Studio diff windows using `IVsDifferenceService`.
  - `DiagnosticsService.cs`: Feeds live build errors and compiler warnings into prompt context.
- **Persistence**: `StorageService.cs` stores local chats, messages, and prompt templates in `%LocalAppData%\GeminiVS\gemini_vs.db` with SQLite FTS5 search.

---

## 📁 Solution Structure

```text
vs-extension/
├── Gemini.VisualStudio.sln               # Visual Studio 2022 Solution
├── Gemini.Extension/                     # VSIX Package Project (.NET Framework 4.7.2)
│   ├── GeminiPackage.cs                  # AsyncPackage initialization
│   ├── source.extension.vsixmanifest     # VSIX manifest targeting VS 2022 (17.0+)
│   ├── VSCommandTable.vsct               # Menu command (View -> Other Windows -> Gemini Desktop)
│   ├── Commands/
│   │   ├── GeminiToolWindowCommand.cs    # Tool window toggle command handler
│   │   └── GeminiEditorCommands.cs       # Right-click editor & error list commands
│   ├── UI/
│   │   ├── GeminiToolWindow.cs           # ToolWindowPane container
│   │   ├── GeminiToolWindowControl.xaml  # WPF UserControl hosting WebView2
│   │   └── GeminiToolWindowControl.xaml.cs # CoreWebView2 virtual host & theme sync
│   ├── Bridge/
│   │   └── WebView2Bridge.cs             # C# <-> JS JSON-RPC message dispatcher
│   ├── Services/
│   │   ├── ProcessSupervisor.cs          # Win32 Job Objects child process governor
│   │   ├── AcpClient.cs                  # Agent Client Protocol (JSON-RPC 2.0) client
│   │   ├── IdeContextService.cs          # Active file, selection, and solution context
│   │   ├── NativeDiffService.cs          # Visual Studio IVsDifferenceService side-by-side diff
│   │   ├── DiagnosticsService.cs         # Compiler error list and diagnostics
│   │   └── StorageService.cs             # Local SQLite database & FTS5 search
│   └── Options/
│       └── GeminiOptionsPage.cs          # Tools -> Options -> Gemini settings page
```

---

## ⚡ Key Commands & Shortcuts

| Action | Shortcut | Location |
| :--- | :--- | :--- |
| **Open Gemini Desktop** | `Ctrl+Alt+G` | **View → Other Windows → Gemini Desktop** |
| **Ask Gemini about Selection** | `Ctrl+Alt+A` | **Right-click selected code** in Code Editor |
| **Explain Code with Gemini** | — | **Right-click selected code** → *Explain Code with Gemini* |
| **Fix Build Errors** | — | **Right-click Error List** → *Fix Errors with Gemini* |

---

## 🛠️ Prerequisites

1. **Visual Studio 2022** (v17.0 or later: Community, Professional, or Enterprise).
2. Workloads required in Visual Studio Installer:
   - **Visual Studio extension development** (includes VSSDK and build tools).
   - **.NET desktop development** (.NET Framework 4.7.2 targeting pack).
3. **Microsoft Edge WebView2 Runtime** (installed by default on modern Windows 10/11 and with VS 2022).
4. **Gemini CLI**:
   ```powershell
   npm install -g @google/gemini-cli
   ```

---

## 🚀 Building & Debugging

1. Build and bundle the frontend web assets into the VSIX project:
   ```powershell
   npm run build:vsix
   ```
2. Open `vs-extension\Gemini.VisualStudio.sln` in **Visual Studio 2022**.
3. Set `Gemini.Extension` as the startup project.
4. Press **F5** to launch the **Visual Studio Experimental Instance** (`/rootsuffix Exp`).
5. In the experimental instance:
   - Open any `.sln` or code folder.
   - Right-click any code selection and choose **Ask Gemini about Selection** (or press `Ctrl+Alt+A` / `Ctrl+Alt+G`).
   - The Gemini tool window will dock into the IDE with full solution awareness!

---

## 📦 Packaging & Distribution

To create the distributable `.vsix` installer file:
1. Select **Release** configuration in Visual Studio 2022.
2. Build the solution (`Build -> Build Solution`).
3. The compiled extension installer will be output to:
   ```text
   vs-extension\Gemini.Extension\bin\Release\Gemini.Extension.vsix
   ```
4. Double-click the `.vsix` file to install it into your main Visual Studio 2022 installation, or upload it to the [Visual Studio Marketplace](https://marketplace.visualstudio.com/).
