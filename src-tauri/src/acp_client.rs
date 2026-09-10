use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;
use tokio::sync::Mutex;
use std::io::Write;
use tauri::{AppHandle, Emitter};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct JsonRpcRequest {
    pub jsonrpc: String,
    pub id: u64,
    pub method: String,
    #[serde(skip_serializing_if = "Value::is_null")]
    pub params: Value,
}

#[allow(dead_code)]
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct JsonRpcResponse {
    pub jsonrpc: String,
    pub id: Option<u64>,
    pub result: Option<Value>,
    pub error: Option<JsonRpcError>,
}

#[allow(dead_code)]
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct JsonRpcError {
    pub code: i64,
    pub message: String,
    pub data: Option<Value>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StreamChunkPayload {
    pub session_id: String,
    pub delta: String,
    pub is_done: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolPermissionPayload {
    pub request_id: u64,
    pub tool_name: String,
    pub parameters: Value,
    pub reason: Option<String>,
}

pub struct AcpSession {
    next_id: AtomicU64,
    stdin_writer: Arc<Mutex<Option<Box<dyn Write + Send>>>>,
}

impl AcpSession {
    pub fn new() -> Self {
        Self {
            next_id: AtomicU64::new(1),
            stdin_writer: Arc::new(Mutex::new(None)),
        }
    }

    pub async fn set_stdin(&self, writer: Box<dyn Write + Send>) {
        let mut guard = self.stdin_writer.lock().await;
        *guard = Some(writer);
    }

    pub fn next_id(&self) -> u64 {
        self.next_id.fetch_add(1, Ordering::SeqCst)
    }

    pub async fn send_request(&self, method: &str, params: Value) -> Result<u64, String> {
        let id = self.next_id();
        let req = JsonRpcRequest {
            jsonrpc: "2.0".to_string(),
            id,
            method: method.to_string(),
            params,
        };

        let json_line = serde_json::to_string(&req).map_err(|e| e.to_string())? + "\n";

        let mut guard = self.stdin_writer.lock().await;
        if let Some(writer) = guard.as_mut() {
            writer.write_all(json_line.as_bytes()).map_err(|e| e.to_string())?;
            writer.flush().map_err(|e| e.to_string())?;
            Ok(id)
        } else {
            Err("CLI stdin is not connected".to_string())
        }
    }

    pub async fn send_response(&self, id: u64, result: Value) -> Result<(), String> {
        let resp = serde_json::json!({
            "jsonrpc": "2.0",
            "id": id,
            "result": result
        });
        let json_line = serde_json::to_string(&resp).map_err(|e| e.to_string())? + "\n";

        let mut guard = self.stdin_writer.lock().await;
        if let Some(writer) = guard.as_mut() {
            writer.write_all(json_line.as_bytes()).map_err(|e| e.to_string())?;
            writer.flush().map_err(|e| e.to_string())?;
            Ok(())
        } else {
            Err("CLI stdin is not connected".to_string())
        }
    }

    pub async fn send_cancel(&self, session_id: Option<&str>, target_request_id: Option<u64>) -> Result<(), String> {
        let mut params = serde_json::Map::new();
        if let Some(sid) = session_id {
            params.insert("sessionId".to_string(), serde_json::Value::String(sid.to_string()));
        }
        if let Some(rid) = target_request_id {
            params.insert("id".to_string(), serde_json::json!(rid));
        }
        self.send_request("session/cancel", serde_json::Value::Object(params)).await?;
        Ok(())
    }
}

pub fn handle_acp_line(line: &str, app_handle: &AppHandle, active_session_id: &str) {
    let trimmed = line.trim();
    if trimmed.is_empty() {
        return;
    }

    // Try parsing as JSON-RPC object
    if let Ok(val) = serde_json::from_str::<Value>(trimmed) {
        if let Some(method) = val.get("method").and_then(|m| m.as_str()) {
            match method {
                // Streaming chunk notification
                "session/update" | "acp/chunk" | "content/delta" | "stream" => {
                    // Check standard ACP format (update.content.text or update.delta) as well as flat fields
                    let delta = val.pointer("/params/update/content/text")
                        .or_else(|| val.pointer("/params/update/text"))
                        .or_else(|| val.pointer("/params/update/delta"))
                        .or_else(|| val.pointer("/params/update/content"))
                        .or_else(|| val.pointer("/params/delta"))
                        .or_else(|| val.pointer("/params/content"))
                        .or_else(|| val.pointer("/params/text"))
                        .and_then(|t| t.as_str())
                        .unwrap_or("")
                        .to_string();

                    let is_done = val.pointer("/params/done")
                        .or_else(|| val.pointer("/params/update/done"))
                        .and_then(|d| d.as_bool())
                        .unwrap_or(false);

                    let _ = app_handle.emit("acp-chunk", StreamChunkPayload {
                        session_id: active_session_id.to_string(),
                        delta,
                        is_done,
                    });
                }
                // Interactive tool confirmation request
                "permission/request" | "session/permission_request" | "session/request_permission" | "tool/confirm" => {
                    let req_id = val.get("id").and_then(|id| id.as_u64()).unwrap_or(0);
                    let tool_name = val.pointer("/params/tool")
                        .or_else(|| val.pointer("/params/name"))
                        .and_then(|t| t.as_str())
                        .unwrap_or("unknown")
                        .to_string();

                    let parameters = val.pointer("/params/arguments")
                        .or_else(|| val.pointer("/params/parameters"))
                        .cloned()
                        .unwrap_or(Value::Null);

                    let reason = val.pointer("/params/reason")
                        .and_then(|r| r.as_str())
                        .map(|s| s.to_string());

                    let _ = app_handle.emit("acp-tool-permission", ToolPermissionPayload {
                        request_id: req_id,
                        tool_name,
                        parameters,
                        reason,
                    });
                }
                _ => {
                    // Forward generic notification
                    let _ = app_handle.emit("acp-notification", val);
                }
            }
            return;
        }

        // Check if response has a result containing text/content or turn completion
        if val.get("result").is_some() {
            if let Some(text) = val.pointer("/result/content/text")
                .or_else(|| val.pointer("/result/content"))
                .or_else(|| val.pointer("/result/text"))
                .or_else(|| val.pointer("/result/output"))
                .and_then(|t| t.as_str()) {
                let _ = app_handle.emit("acp-chunk", StreamChunkPayload {
                    session_id: active_session_id.to_string(),
                    delta: text.to_string(),
                    is_done: true,
                });
            } else if val.pointer("/result/stopReason").is_some() {
                // ACP turn completion notification
                let _ = app_handle.emit("acp-chunk", StreamChunkPayload {
                    session_id: active_session_id.to_string(),
                    delta: "".to_string(),
                    is_done: true,
                });
            } else {
                let _ = app_handle.emit("acp-response", val);
            }
            return;
        }

        if val.get("error").is_some() {
            let _ = app_handle.emit("acp-error", val);
            return;
        }
    }

    // Fallback: If CLI outputs raw streaming lines or debug logs, emit as text chunk
    let _ = app_handle.emit("acp-chunk", StreamChunkPayload {
        session_id: active_session_id.to_string(),
        delta: format!("{}\n", trimmed),
        is_done: false,
    });
}
