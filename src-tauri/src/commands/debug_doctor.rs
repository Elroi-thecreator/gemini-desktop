use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileSnippetResult {
    pub file_path: String,
    pub file_name: String,
    pub target_line: usize,
    pub start_line: usize,
    pub end_line: usize,
    pub snippet: String,
    pub total_lines: usize,
    pub exists: bool,
    pub language: String,
}

/// Detects programming language identifier for syntax highlighting based on file extension
pub fn detect_source_language(path_or_name: &str) -> String {
    let lower = path_or_name.to_lowercase();
    if lower.ends_with(".cs") {
        "csharp".to_string()
    } else if lower.ends_with(".ts") {
        "typescript".to_string()
    } else if lower.ends_with(".tsx") {
        "tsx".to_string()
    } else if lower.ends_with(".js") || lower.ends_with(".mjs") || lower.ends_with(".cjs") {
        "javascript".to_string()
    } else if lower.ends_with(".jsx") {
        "jsx".to_string()
    } else if lower.ends_with(".rs") {
        "rust".to_string()
    } else if lower.ends_with(".py") {
        "python".to_string()
    } else if lower.ends_with(".cpp") || lower.ends_with(".cxx") || lower.ends_with(".cc") || lower.ends_with(".hpp") || lower.ends_with(".h") {
        "cpp".to_string()
    } else if lower.ends_with(".c") {
        "c".to_string()
    } else if lower.ends_with(".go") {
        "go".to_string()
    } else if lower.ends_with(".java") {
        "java".to_string()
    } else if lower.ends_with(".svelte") {
        "svelte".to_string()
    } else if lower.ends_with(".html") || lower.ends_with(".htm") {
        "html".to_string()
    } else if lower.ends_with(".css") {
        "css".to_string()
    } else if lower.ends_with(".sql") {
        "sql".to_string()
    } else if lower.ends_with(".json") {
        "json".to_string()
    } else if lower.ends_with(".xml") || lower.ends_with(".csproj") || lower.ends_with(".vbproj") {
        "xml".to_string()
    } else {
        "plaintext".to_string()
    }
}

/// Resolves a file path against disk or active workspace root
pub fn resolve_source_file_path(file_path: &str, workspace_path: Option<&str>) -> Option<PathBuf> {
    let raw_path = PathBuf::from(file_path.trim());
    if raw_path.exists() && raw_path.is_file() {
        return Some(raw_path);
    }

    // If not found and a workspace path is supplied, attempt resolution within workspace
    if let Some(ws) = workspace_path {
        let ws_root = PathBuf::from(ws.trim());
        if ws_root.exists() && ws_root.is_dir() {
            // Case 1: direct join with relative path
            let candidate1 = ws_root.join(file_path.trim_start_matches('/').trim_start_matches('\\'));
            if candidate1.exists() && candidate1.is_file() {
                return Some(candidate1);
            }

            // Case 2: match file name only
            if let Some(file_name) = raw_path.file_name() {
                if let Ok(name_str) = file_name.to_os_string().into_string() {
                    let mut found: Option<PathBuf> = None;
                    walk_locate_file(&ws_root, &name_str, 0, 5, &mut found);
                    if let Some(p) = found {
                        return Some(p);
                    }
                }
            }
        }
    }

    None
}

fn walk_locate_file(dir: &Path, target_name: &str, depth: usize, max_depth: usize, out: &mut Option<PathBuf>) {
    if depth > max_depth || out.is_some() {
        return;
    }
    if let Ok(read_dir) = std::fs::read_dir(dir) {
        for entry in read_dir.flatten() {
            let path = entry.path();
            if path.is_file() {
                if let Some(name) = path.file_name() {
                    if name.to_string_lossy().eq_ignore_ascii_case(target_name) {
                        *out = Some(path);
                        return;
                    }
                }
            } else if path.is_dir() {
                let dir_name = path.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
                if !dir_name.starts_with('.') && dir_name != "node_modules" && dir_name != "bin" && dir_name != "obj" && dir_name != "target" {
                    walk_locate_file(&path, target_name, depth + 1, max_depth, out);
                    if out.is_some() {
                        return;
                    }
                }
            }
        }
    }
}

/// Reads source lines surrounding a crashing line for high-context debugging prompts
#[tauri::command]
pub async fn read_file_snippet(
    file_path: String,
    target_line: usize,
    context_lines: Option<usize>,
    workspace_path: Option<String>,
) -> Result<FileSnippetResult, String> {
    tokio::task::spawn_blocking(move || {
        read_file_snippet_internal(&file_path, target_line, context_lines.unwrap_or(15), workspace_path.as_deref())
    })
    .await
    .map_err(|e| format!("Snippet read failed: {}", e))
}

pub fn read_file_snippet_internal(
    file_path: &str,
    target_line: usize,
    context_window: usize,
    workspace_path: Option<&str>,
) -> FileSnippetResult {
    let resolved = resolve_source_file_path(file_path, workspace_path);
    let raw_name = Path::new(file_path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_else(|| "UnknownFile".to_string());
    let language = detect_source_language(file_path);

    let (final_path, file_content) = match resolved {
        Some(path) => {
            let path_str = path.to_string_lossy().to_string();
            match std::fs::read_to_string(&path) {
                Ok(content) => (path_str, Some(content)),
                Err(_) => {
                    // Fallback to lossy read for binary/special encodings
                    match std::fs::read(&path) {
                        Ok(bytes) => (path_str, Some(String::from_utf8_lossy(&bytes).to_string())),
                        Err(_) => (path_str, None),
                    }
                }
            }
        }
        None => (file_path.to_string(), None),
    };

    match file_content {
        Some(content) => {
            let lines: Vec<&str> = content.lines().collect();
            let total_lines = lines.len();
            let safe_target = if target_line == 0 { 1 } else { target_line };

            let start_line = if safe_target > context_window {
                safe_target - context_window
            } else {
                1
            };
            let end_line = (safe_target + context_window).min(total_lines.max(1));

            let mut formatted = String::new();
            for line_idx in start_line..=end_line {
                let actual_line_text = if line_idx <= total_lines {
                    lines[line_idx - 1]
                } else {
                    ""
                };

                let marker = if line_idx == safe_target { ">" } else { " " };
                formatted.push_str(&format!("{} {:4} | {}\n", marker, line_idx, actual_line_text));
            }

            FileSnippetResult {
                file_path: final_path,
                file_name: raw_name,
                target_line: safe_target,
                start_line,
                end_line,
                snippet: formatted.trim_end().to_string(),
                total_lines,
                exists: true,
                language,
            }
        }
        None => FileSnippetResult {
            file_path: final_path,
            file_name: raw_name,
            target_line,
            start_line: target_line,
            end_line: target_line,
            snippet: String::new(),
            total_lines: 0,
            exists: false,
            language,
        },
    }
}

/// Reads the system clipboard safely on Windows
#[tauri::command]
pub fn get_clipboard_text() -> Result<String, String> {
    get_system_clipboard()
}

#[cfg(target_os = "windows")]
pub fn get_system_clipboard() -> Result<String, String> {
    use windows_sys::Win32::System::DataExchange::{CloseClipboard, GetClipboardData, OpenClipboard};
    use windows_sys::Win32::System::Memory::{GlobalLock, GlobalUnlock};
    const CF_UNICODETEXT: u32 = 13;

    unsafe {
        // Try to open clipboard with retry in case transiently locked by another process
        let mut opened = false;
        for _ in 0..5 {
            if OpenClipboard(std::ptr::null_mut()) != 0 {
                opened = true;
                break;
            }
            std::thread::sleep(std::time::Duration::from_millis(15));
        }

        if !opened {
            return Err("Clipboard is currently busy or locked by another process.".to_string());
        }

        let handle = GetClipboardData(CF_UNICODETEXT);
        if handle.is_null() {
            CloseClipboard();
            return Ok(String::new());
        }

        let ptr = GlobalLock(handle) as *const u16;
        if ptr.is_null() {
            CloseClipboard();
            return Ok(String::new());
        }

        let mut len = 0;
        while *ptr.add(len) != 0 {
            len += 1;
        }

        let slice = std::slice::from_raw_parts(ptr, len);
        let result = String::from_utf16_lossy(slice);

        GlobalUnlock(handle);
        CloseClipboard();

        Ok(result)
    }
}

#[cfg(not(target_os = "windows"))]
pub fn get_system_clipboard() -> Result<String, String> {
    Ok(String::new())
}

/// Registers the global system-wide hotkey (Ctrl+Alt+D) on Windows
#[cfg(target_os = "windows")]
pub fn register_global_debug_hotkey(app: tauri::AppHandle) {
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{RegisterHotKey, MOD_ALT, MOD_CONTROL, MOD_NOREPEAT};
    use windows_sys::Win32::UI::WindowsAndMessaging::{GetMessageW, MSG, WM_HOTKEY};
    use tauri::{Emitter, Manager};

    const HOTKEY_ID: i32 = 0x4444; // 'DD' (Debug Doctor)
    const VK_D: u32 = 0x44; // 'D'

    unsafe {
        // Register Ctrl+Alt+D system-wide
        if RegisterHotKey(std::ptr::null_mut(), HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_D) == 0 {
            eprintln!("[DebugDoctor] Note: Could not register Ctrl+Alt+D system hotkey (might be occupied).");
            return;
        }

        let mut msg: MSG = std::mem::zeroed();
        while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
            if msg.message == WM_HOTKEY && msg.wParam == HOTKEY_ID as usize {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.unminimize();
                    let _ = window.set_focus();
                    let _ = window.emit("instant-debug-doctor", ());
                }
            }
        }
    }
}

#[cfg(not(target_os = "windows"))]
pub fn register_global_debug_hotkey(_app: tauri::AppHandle) {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_detect_source_language() {
        assert_eq!(detect_source_language("OrderService.cs"), "csharp");
        assert_eq!(detect_source_language("C:\\src\\PaymentController.cs"), "csharp");
        assert_eq!(detect_source_language("app.ts"), "typescript");
        assert_eq!(detect_source_language("Component.tsx"), "tsx");
        assert_eq!(detect_source_language("main.rs"), "rust");
        assert_eq!(detect_source_language("script.py"), "python");
        assert_eq!(detect_source_language("native.cpp"), "cpp");
        assert_eq!(detect_source_language("unknown.xyz"), "plaintext");
    }

    #[test]
    fn test_read_file_snippet_internal_success() {
        let temp_dir = std::env::temp_dir().join(format!("gemini_test_snippet_{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&temp_dir).unwrap();
        let file_path = temp_dir.join("Calculator.cs");

        let code = "\
namespace Demo;
public class Calculator
{
    public int Divide(int a, int b)
    {
        if (b == 0)
        {
            throw new System.DivideByZeroException(\"Cannot divide by zero\");
        }
        return a / b;
    }
}";
        std::fs::write(&file_path, code).unwrap();

        let path_str = file_path.to_string_lossy().to_string();
        // Target line 8 is the throw statement
        let snippet = read_file_snippet_internal(&path_str, 8, 3, None);

        assert!(snippet.exists);
        assert_eq!(snippet.file_name, "Calculator.cs");
        assert_eq!(snippet.target_line, 8);
        assert_eq!(snippet.start_line, 5); // 8 - 3
        assert_eq!(snippet.end_line, 11);  // 8 + 3
        assert_eq!(snippet.language, "csharp");
        assert!(snippet.snippet.contains(">    8 |"));
        assert!(snippet.snippet.contains("DivideByZeroException"));
        assert!(snippet.snippet.contains("   5 |"));

        let _ = std::fs::remove_dir_all(&temp_dir);
    }

    #[test]
    fn test_read_file_snippet_nonexistent() {
        let snippet = read_file_snippet_internal("C:\\definitely\\nonexistent\\file.cs", 42, 10, None);
        assert!(!snippet.exists);
        assert_eq!(snippet.snippet, "");
        assert_eq!(snippet.language, "csharp");
    }

    #[test]
    fn test_read_file_snippet_resolve_in_workspace() {
        let temp_dir = std::env::temp_dir().join(format!("gemini_test_ws_res_{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&temp_dir.join("Services")).unwrap();
        let file_path = temp_dir.join("Services").join("OrderService.cs");
        std::fs::write(&file_path, "public class OrderService {}").unwrap();

        let ws_str = temp_dir.to_string_lossy().to_string();
        // Pass relative path "Services/OrderService.cs" with workspace path
        let snippet = read_file_snippet_internal("Services/OrderService.cs", 1, 5, Some(&ws_str));
        assert!(snippet.exists);
        assert_eq!(snippet.file_name, "OrderService.cs");

        // Pass filename only "OrderService.cs"
        let snippet2 = read_file_snippet_internal("OrderService.cs", 1, 5, Some(&ws_str));
        assert!(snippet2.exists);
        assert_eq!(snippet2.file_name, "OrderService.cs");

        let _ = std::fs::remove_dir_all(&temp_dir);
    }

    #[test]
    fn test_get_clipboard_text_does_not_panic() {
        let res = get_clipboard_text();
        assert!(res.is_ok() || res.is_err());
    }
}
