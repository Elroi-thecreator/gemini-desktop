# 🚀 Gemini Desktop

[![Release](https://img.shields.io/github/v/release/sureshjsreddy/gemini-desktop?style=flat-square&color=38bdf8)](https://github.com/sureshjsreddy/gemini-desktop/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)
[![Tauri v2](https://img.shields.io/badge/Tauri-v2-blue?style=flat-square&logo=tauri)](https://v2.tauri.app)
[![Svelte 5](https://img.shields.io/badge/Svelte-5-FF3E00?style=flat-square&logo=svelte)](https://svelte.dev)
[![Rust](https://img.shields.io/badge/Rust-2021-black?style=flat-square&logo=rust)](https://www.rust-lang.org)
[![Tailwind CSS v4](https://img.shields.io/badge/Tailwind-v4-38bdf8?style=flat-square&logo=tailwindcss)](https://tailwindcss.com)

A modern, blazing-fast native desktop application for interacting with the **Gemini CLI** via the **Agent Client Protocol (ACP)** over standard I/O (JSON-RPC). Built from the ground up with **Tauri v2**, **Rust**, and **SvelteKit / Svelte 5**.

---

## ✨ Features

- **⚡ Native Agent Client Protocol (ACP)**
  - Full bidirectional JSON-RPC streaming directly over the child CLI process `stdin`/`stdout`.
  - Real-time token streaming with cancelable requests.
  - Interactive **Tool Execution Permission Intercepts** — review and approve tool/command execution before changes occur on your machine.

- **🎯 Official Gemini Model Selection**
  - **Auto (Gemini 3)**: Dynamically balances between Pro and Flash models based on task complexity.
  - **Auto (Gemini 2.5)**: Dynamic selection across Gemini 2.5 series.
  - **Gemini 3 Series**: `gemini-3-pro-preview` (deep reasoning & architecture), `gemini-3-flash-preview` (high speed).
  - **Gemini 2.5 Series**: `gemini-2.5-pro`, `gemini-2.5-flash`, and `gemini-2.5-flash-lite`.
  - **Manual / Custom Entry**: Enter any custom model identifier, fine-tuned endpoint, or preview string.

- **📁 Workspace & Directory Profiles**
  - Configure individual workspaces bound to local project directories (`GEMINI.md` context is automatically loaded).
  - Customize preferred models and custom system instructions per workspace profile.

- **💬 Multi-Session Chat & History**
  - Seamlessly switch between multiple conversation threads per workspace.
  - Rename, export (Markdown/JSON), and delete sessions with ease.

- **🔍 Full-Text Search (SQLite FTS5)**
  - Embedded local-first SQLite database.
  - Instant full-text search across previous conversations, code snippets, and responses.

- **📚 Reusable Prompt Templates**
  - Built-in prompt library for common developer workflows (Architecture Analysis, Test Generation, Code Refactoring, Documentation, Dead Code Detection).
  - Add your own custom prompt templates with category filtering.

- **🛡️ Process Management & Safety**
  - Supervised with native **Windows Job Objects** (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`).
  - Guarantees child CLI processes are terminated cleanly on exit without leaving orphaned background tasks.
  - Fallback mock / offline mode when the CLI is in standby.

---

## 📥 Installation & Downloads

Pre-built Windows binaries are automatically generated on every release:

1. Visit the [**Latest Releases**](https://github.com/sureshjsreddy/gemini-desktop/releases/latest) page.
2. Download either:
   - **`GeminiDesktop-Setup.exe`**: Standard Windows installer (NSIS).
   - **`Gemini Desktop_x64_en-US.msi`**: Windows MSI installer.
   - **`GeminiDesktop.exe`**: Portable standalone executable.
3. Run the installer or portable binary.

### Prerequisites
- **Operating System**: Windows 10/11 (64-bit).
- **Gemini CLI**: Ensure `gemini` is installed and accessible in your system `PATH`.

---

## 🛠️ Tech Stack

| Layer | Technology |
| :--- | :--- |
| **Desktop Framework** | [Tauri v2](https://v2.tauri.app) |
| **Backend Language** | [Rust](https://www.rust-lang.org) (2021 Edition) |
| **Frontend Framework** | [SvelteKit 2](https://kit.svelte.dev) + [Svelte 5](https://svelte.dev) (Runes) |
| **Language** | [TypeScript](https://www.typescriptlang.org) |
| **Styling** | [Tailwind CSS v4](https://tailwindcss.com) + [Lucide Icons](https://lucide.dev) |
| **Markdown & Syntax** | `marked`, `highlight.js` |
| **Database** | Embedded SQLite with FTS5 via `rusqlite` |

---

## 💻 Local Development Setup

### 1. Prerequisites
- **Node.js**: `v20` or later
- **Rust**: Latest stable toolchain ([rustup.rs](https://rustup.rs))
- **Tauri CLI**: Installed via npm or cargo

### 2. Clone & Install
```bash
git clone https://github.com/sureshjsreddy/gemini-desktop.git
cd gemini-desktop

# Install frontend dependencies
npm install
```

### 3. Run Development Server
```bash
npm run tauri dev
```
This boots Vite with hot-module reloading on `localhost:1420` and spawns the native Tauri desktop window.

### 4. Code Quality & Verification
```bash
# Type check Svelte & TypeScript
npm run check

# Run Rust unit tests
npm --prefix src-tauri test
# or
cargo test --manifest-path src-tauri/Cargo.toml
```

### 5. Build for Production
```bash
npm run tauri build
```
Compiled binaries and installers will be output to `src-tauri/target/release/bundle/`.

---

## 🤖 CI / CD

Automated builds and releases are managed via **GitHub Actions** (`.github/workflows/release.yml`):
- Pushing any tag matching `v*` (e.g. `v0.1.0`) triggers a build on `windows-latest`.
- Compiles the SvelteKit frontend and Rust release binary with LTO optimizations.
- Automatically publishes the release and attaches `.exe` and `.msi` installers to GitHub Releases.
- Manual triggers are also supported via `workflow_dispatch` in the GitHub Actions tab.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
