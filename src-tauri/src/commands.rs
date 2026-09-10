use crate::database::{DbManager, Message, PromptTemplate, SearchResult, Session, Workspace};
use crate::process_manager::ProcessSupervisor;
use crate::acp_client::{handle_acp_line, AcpSession};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::io::{BufRead, BufReader};
use std::path::PathBuf;
use std::process::Command;
use std::sync::Arc;
use tauri::{AppHandle, State, Emitter};
use tokio::sync::Mutex;
use uuid::Uuid;
use chrono::Utc;

#[derive(Debug, Serialize, Deserialize)]
pub struct GeminiEnvStatus {
    pub installed: bool,
    pub path: Option<String>,
    pub version: Option<String>,
    pub details: String,
}

pub struct AppState {
    pub db: DbManager,
    pub supervisor: ProcessSupervisor,
    pub acp_session: Arc<AcpSession>,
    pub active_process_workspace: Arc<Mutex<Option<String>>>,
    pub initialized_sessions: Arc<Mutex<HashSet<String>>>,
}

#[tauri::command]
pub async fn check_gemini_env() -> Result<GeminiEnvStatus, String> {
    match crate::process_manager::find_gemini_executable() {
        Some(bin_path) => {
            let path_str = bin_path.to_string_lossy().to_string();
            let is_batch = path_str.to_lowercase().ends_with(".cmd")
                        || path_str.to_lowercase().ends_with(".bat");

            #[cfg(target_os = "windows")]
            let ver_check = if is_batch {
                Command::new("cmd.exe")
                    .args(["/c", &path_str, "--version"])
                    .output()
            } else {
                Command::new(&path_str)
                    .arg("--version")
                    .output()
            };

            #[cfg(not(target_os = "windows"))]
            let ver_check = Command::new(&path_str)
                .arg("--version")
                .output();

            let version = if let Ok(ver_out) = ver_check {
                if ver_out.status.success() {
                    let v = String::from_utf8_lossy(&ver_out.stdout).trim().to_string();
                    if v.is_empty() {
                        "installed".to_string()
                    } else {
                        v
                    }
                } else {
                    "installed".to_string()
                }
            } else {
                "installed".to_string()
            };

            Ok(GeminiEnvStatus {
                installed: true,
                path: Some(path_str),
                version: Some(version),
                details: "Gemini CLI found and verified on system.".to_string(),
            })
        }
        None => Ok(GeminiEnvStatus {
            installed: false,
            path: None,
            version: None,
            details: "Gemini CLI not found in PATH or standard npm/pnpm locations.".to_string(),
        }),
    }
}

#[tauri::command]
pub fn get_workspaces(state: State<AppState>) -> Result<Vec<Workspace>, String> {
    state.db.list_workspaces()
}

#[tauri::command]
pub fn save_workspace(state: State<AppState>, workspace: Workspace) -> Result<(), String> {
    state.db.save_workspace(workspace)
}

#[tauri::command]
pub fn delete_workspace(state: State<AppState>, id: String) -> Result<(), String> {
    state.db.delete_workspace(&id)
}

#[tauri::command]
pub fn get_sessions(state: State<AppState>, workspace_id: String) -> Result<Vec<Session>, String> {
    state.db.list_sessions(&workspace_id)
}

#[tauri::command]
pub fn create_session(state: State<AppState>, workspace_id: String, title: String) -> Result<Session, String> {
    state.db.create_session(&workspace_id, &title)
}

#[tauri::command]
pub fn rename_session(state: State<AppState>, session_id: String, title: String) -> Result<(), String> {
    state.db.rename_session(&session_id, &title)
}

#[tauri::command]
pub async fn delete_session(state: State<'_, AppState>, session_id: String) -> Result<(), String> {
    let mut sessions_guard = state.initialized_sessions.lock().await;
    sessions_guard.remove(&session_id);
    state.db.delete_session(&session_id)
}

#[tauri::command]
pub fn get_session_messages(state: State<AppState>, session_id: String) -> Result<Vec<Message>, String> {
    state.db.list_messages(&session_id)
}

#[tauri::command]
pub fn save_message(state: State<AppState>, msg: Message) -> Result<(), String> {
    state.db.save_message(msg)
}

#[tauri::command]
pub async fn send_prompt(
    app: AppHandle,
    state: State<'_, AppState>,
    session_id: String,
    workspace_id: String,
    prompt: String,
    model: String,
) -> Result<u64, String> {
    let now = Utc::now().to_rfc3339();

    // 1. Save user prompt to local database
    let user_msg = Message {
        id: format!("msg-{}", Uuid::new_v4()),
        session_id: session_id.clone(),
        role: "user".to_string(),
        content: prompt.clone(),
        tool_calls_json: None,
        token_count: 0,
        created_at: now.clone(),
    };
    state.db.save_message(user_msg)?;

    // 2. Determine workspace path
    let workspaces = state.db.list_workspaces()?;
    let ws = workspaces.into_iter().find(|w| w.id == workspace_id);
    let ws_path = ws.as_ref().map(|w| PathBuf::from(&w.path));

    // 3. Ensure CLI process is spawned and ACP connected
    let mut ws_guard = state.active_process_workspace.lock().await;
    let needs_spawn = match &*ws_guard {
        Some(current_ws) => current_ws != &workspace_id,
        None => true,
    };

    if needs_spawn {
        {
            let mut sessions_guard = state.initialized_sessions.lock().await;
            sessions_guard.clear();
        }

        let gemini_bin = match crate::process_manager::find_gemini_executable() {
            Some(p) => p,
            None => {
                let assistant_id = format!("msg-{}", Uuid::new_v4());
                let fallback_text = format!(
                    "**[Offline / Mock Mode]**\n\nGemini CLI could not be located on your system PATH or npm global directories.\n\nPrompt received:\n> {}\n\nPlease install Gemini CLI (`npm install -g @google/gemini-cli` or `scoop install gemini-cli`) and ensure it is in your PATH.",
                    prompt
                );
                let assistant_msg = Message {
                    id: assistant_id,
                    session_id: session_id.clone(),
                    role: "assistant".to_string(),
                    content: fallback_text.clone(),
                    tool_calls_json: None,
                    token_count: 50,
                    created_at: Utc::now().to_rfc3339(),
                };
                let _ = state.db.save_message(assistant_msg);

                let _ = app.emit("acp-chunk", crate::acp_client::StreamChunkPayload {
                    session_id: session_id.clone(),
                    delta: fallback_text,
                    is_done: true,
                });
                return Ok(0);
            }
        };

        match state.supervisor.spawn_gemini(&gemini_bin, ws_path.clone(), &[]) {
            Ok(mut child) => {
                if let Some(stdin) = child.stdin.take() {
                    state.acp_session.set_stdin(Box::new(stdin)).await;
                }

                if let Some(stdout) = child.stdout.take() {
                    let app_clone = app.clone();
                    let session_clone = session_id.clone();
                    std::thread::spawn(move || {
                        let reader = BufReader::new(stdout);
                        for line in reader.lines() {
                            if let Ok(l) = line {
                                handle_acp_line(&l, &app_clone, &session_clone);
                            }
                        }
                    });
                }

                if let Some(stderr) = child.stderr.take() {
                    let app_clone = app.clone();
                    std::thread::spawn(move || {
                        let reader = BufReader::new(stderr);
                        for line in reader.lines() {
                            if let Ok(l) = line {
                                let _ = app_clone.emit("acp-stderr", l);
                            }
                        }
                    });
                }

                *ws_guard = Some(workspace_id.clone());

                // Send initialize request (per ACP specification)
                let _ = state.acp_session.send_request("initialize", serde_json::json!({
                    "protocolVersion": 1,
                    "clientInfo": {
                        "name": "GeminiDesktop",
                        "version": "0.2.0"
                    }
                })).await;
            }
            Err(e) => {
                let assistant_id = format!("msg-{}", Uuid::new_v4());
                let fallback_text = format!(
                    "**[Offline / Mock Mode]**\n\nFailed to launch Gemini CLI at `{}` (`{}`).\n\nPrompt received:\n> {}\n\nPlease check permissions or verify your Gemini CLI installation.",
                    gemini_bin.display(), e, prompt
                );
                let assistant_msg = Message {
                    id: assistant_id,
                    session_id: session_id.clone(),
                    role: "assistant".to_string(),
                    content: fallback_text.clone(),
                    tool_calls_json: None,
                    token_count: 50,
                    created_at: Utc::now().to_rfc3339(),
                };
                let _ = state.db.save_message(assistant_msg);

                let _ = app.emit("acp-chunk", crate::acp_client::StreamChunkPayload {
                    session_id: session_id.clone(),
                    delta: fallback_text,
                    is_done: true,
                });
                return Ok(0);
            }
        }
    }

    // 4. Establish session context & send prompt over ACP
    let mut sessions_guard = state.initialized_sessions.lock().await;
    if !sessions_guard.contains(&session_id) {
        let ws_path_str = ws.as_ref().map(|w| w.path.clone()).unwrap_or_else(|| ".".to_string());
        let _ = state.acp_session.send_request("session/new", serde_json::json!({
            "sessionId": session_id,
            "cwd": ws_path_str,
            "mcpServers": [],
            "model": model,
        })).await;
        sessions_guard.insert(session_id.clone());
    }

    let params = serde_json::json!({
        "sessionId": session_id,
        "prompt": [
            {
                "type": "text",
                "text": prompt
            }
        ],
        "model": model
    });

    state.acp_session.send_request("session/prompt", params).await
}

#[tauri::command]
pub async fn cancel_prompt(
    state: State<'_, AppState>,
    request_id: Option<u64>,
    session_id: Option<String>,
) -> Result<(), String> {
    state.acp_session.send_cancel(session_id.as_deref(), request_id).await
}

#[tauri::command]
pub async fn respond_tool_permission(
    state: State<'_, AppState>,
    request_id: u64,
    allowed: bool,
) -> Result<(), String> {
    state.acp_session.send_response(request_id, serde_json::json!({ "allowed": allowed })).await
}

#[tauri::command]
pub fn get_prompts(state: State<AppState>) -> Result<Vec<PromptTemplate>, String> {
    state.db.list_prompts()
}

#[tauri::command]
pub fn search_history(state: State<AppState>, query: String) -> Result<Vec<SearchResult>, String> {
    state.db.search_fts(&query)
}

#[tauri::command]
pub fn export_session(state: State<AppState>, session_id: String, format: String) -> Result<String, String> {
    let messages = state.db.list_messages(&session_id)?;
    match format.as_str() {
        "json" => serde_json::to_string_pretty(&messages).map_err(|e| e.to_string()),
        "txt" => {
            let mut txt = String::new();
            for m in messages {
                txt.push_str(&format!("{}: {}\n\n", m.role.to_uppercase(), m.content));
            }
            Ok(txt)
        }
        _ => {
            // Markdown export
            let mut md = String::new();
            md.push_str(&format!("# Chat Export - Session {}\n\n", session_id));
            for m in messages {
                if m.role == "user" {
                    md.push_str(&format!("### 👤 User\n\n{}\n\n---\n\n", m.content));
                } else {
                    md.push_str(&format!("### ✨ Gemini\n\n{}\n\n---\n\n", m.content));
                }
            }
            Ok(md)
        }
    }
}
