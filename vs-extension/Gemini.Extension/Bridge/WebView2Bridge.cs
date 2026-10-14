using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Gemini.Extension.Bridge
{
    public sealed class WebView2Bridge
    {
        private readonly WebView2 webView;

        public WebView2Bridge(WebView2 webView)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));

            var pkg = GeminiPackage.Instance;
            if (pkg?.AcpClient != null)
            {
                pkg.AcpClient.OnChunkReceived += (sid, delta, isDone) =>
                {
                    DispatchEvent("acp-chunk", new { session_id = sid, delta = delta, is_done = isDone });
                };

                pkg.AcpClient.OnToolPermissionRequested += (reqId, sid, toolName, title) =>
                {
                    DispatchEvent("acp-tool-permission", new
                    {
                        request_id = reqId,
                        session_id = sid,
                        tool_name = toolName,
                        title = title,
                        options = new[]
                        {
                            new { option_id = "allow", name = "Allow Once", kind = "allow_once" },
                            new { option_id = "allow_always", name = "Allow for Session", kind = "allow_always" },
                            new { option_id = "reject", name = "Deny", kind = "reject" }
                        }
                    });
                };

                pkg.AcpClient.OnStderr += (line) =>
                {
                    DispatchEvent("acp-stderr", line);
                };

                pkg.AcpClient.OnError += (err) =>
                {
                    DispatchEvent("acp-error", err);
                };
            }
        }

        public async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(raw)) return;

            try
            {
                using (var doc = JsonDocument.Parse(raw))
                {
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "invoke")
                    {
                        string command = root.GetProperty("command").GetString();
                        string callbackId = root.GetProperty("callbackId").GetString();
                        JsonElement payload = root.TryGetProperty("payload", out var p) ? p : default;

                        await HandleInvokeAsync(command, payload, callbackId);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bridge error handling message: {ex.Message}");
            }
        }

        private async Task HandleInvokeAsync(string command, JsonElement payload, string callbackId)
        {
            var pkg = GeminiPackage.Instance;
            object result = null;
            string errorMessage = null;

            try
            {
                switch (command)
                {
                    case "check_gemini_env":
                        string resolvedCli = pkg?.AcpClient != null ? pkg.AcpClient.FindGeminiCliPath() : null;
                        bool isInstalled = !string.IsNullOrEmpty(resolvedCli) && File.Exists(resolvedCli);
                        result = new
                        {
                            installed = isInstalled,
                            cli_path = resolvedCli ?? "",
                            cli_version = isInstalled ? "auto-detected" : "not found",
                            node_version = "v20.x",
                            auth_method = "oauth",
                            user_email = isInstalled ? "developer@local" : null,
                            is_logged_in = isInstalled,
                            error_message = isInstalled ? (string)null : "Gemini CLI not found on this machine. Please install via 'npm install -g @google/gemini-cli' or configure its path in Tools > Options > Gemini > General."
                        };
                        break;

                    case "get_pending_context_action":
                        result = pkg?.PendingContextAction;
                        if (pkg != null) pkg.PendingContextAction = null;
                        break;

                    case "get_workspaces":
                        string solDir = pkg?.IdeContextService != null ? await pkg.IdeContextService.GetSolutionDirectoryAsync() : "";
                        string solName = !string.IsNullOrEmpty(solDir) ? Path.GetFileName(solDir) : "Active Solution";
                        result = new[]
                        {
                            new
                            {
                                id = "vs-active-solution",
                                name = string.IsNullOrEmpty(solName) ? "Active Solution" : solName,
                                path = solDir,
                                default_model = "gemini-3.8-flash",
                                approval_mode = "default",
                                is_active = true
                            }
                        };
                        break;

                    case "get_sessions":
                        string wsId = payload.TryGetProperty("workspaceId", out var wsp) ? wsp.GetString() : "vs-active-solution";
                        result = pkg?.StorageService != null ? await pkg.StorageService.GetSessionsAsync(wsId) : new System.Collections.Generic.List<Services.SessionRecord>();
                        break;

                    case "create_session":
                        string cWsId = payload.TryGetProperty("workspaceId", out var cw) ? cw.GetString() : "vs-active-solution";
                        string title = payload.TryGetProperty("title", out var ct) ? ct.GetString() : "New Chat";
                        result = pkg?.StorageService != null
                            ? await pkg.StorageService.CreateSessionAsync(cWsId, title)
                            : new Services.SessionRecord { Id = Guid.NewGuid().ToString(), WorkspaceId = cWsId, Title = title };
                        break;

                    case "get_session_messages":
                        string sId = payload.GetProperty("sessionId").GetString();
                        result = pkg?.StorageService != null
                            ? await pkg.StorageService.GetMessagesAsync(sId)
                            : new System.Collections.Generic.List<Services.MessageRecord>();
                        break;

                    case "save_message":
                        var msgProp = payload.GetProperty("msg");
                        var msgRecord = new Services.MessageRecord
                        {
                            Id = msgProp.TryGetProperty("id", out var idp) ? idp.GetString() : Guid.NewGuid().ToString(),
                            SessionId = msgProp.GetProperty("session_id").GetString(),
                            Role = msgProp.GetProperty("role").GetString(),
                            Content = msgProp.GetProperty("content").GetString(),
                            Model = msgProp.TryGetProperty("model", out var mp) && mp.ValueKind == JsonValueKind.String ? mp.GetString() : null,
                            CreatedAt = msgProp.TryGetProperty("created_at", out var cap) ? cap.GetInt64() : 0
                        };
                        if (pkg?.StorageService != null)
                        {
                            await pkg.StorageService.SaveMessageAsync(msgRecord);
                        }
                        result = true;
                        break;

                    case "send_prompt":
                        if (pkg?.AcpClient == null)
                        {
                            errorMessage = "Gemini extension service is initializing. Please try again in a moment.";
                            break;
                        }

                        string promptSid = payload.GetProperty("sessionId").GetString();
                        string promptText = payload.GetProperty("prompt").GetString();
                        string model = payload.TryGetProperty("model", out var modProp) && modProp.ValueKind == JsonValueKind.String ? modProp.GetString() : null;

                        if (!pkg.AcpClient.IsRunning)
                        {
                            string activeSolDir = pkg.IdeContextService != null ? await pkg.IdeContextService.GetSolutionDirectoryAsync() : null;
                            bool started = await pkg.AcpClient.StartAsync(activeSolDir);
                            if (!started)
                            {
                                errorMessage = "Gemini CLI could not be started. Check Tools > Options > Gemini > General or verify Gemini CLI is installed.";
                                break;
                            }
                        }

                        await pkg.AcpClient.SendPromptAsync(promptSid, promptText, model);
                        result = true;
                        break;

                    case "respond_tool_permission":
                        if (pkg?.AcpClient != null)
                        {
                            long reqId = payload.GetProperty("requestId").GetInt64();
                            string optId = payload.TryGetProperty("optionId", out var op) ? op.GetString() : "allow";
                            bool allowed = payload.TryGetProperty("allowed", out var al) ? al.GetBoolean() : true;
                            await pkg.AcpClient.RespondToolPermissionAsync(reqId, optId, allowed);
                        }
                        result = true;
                        break;

                    case "rename_session":
                        if (pkg?.StorageService != null)
                        {
                            string rSid = payload.GetProperty("sessionId").GetString();
                            string rTitle = payload.GetProperty("title").GetString();
                            await pkg.StorageService.RenameSessionAsync(rSid, rTitle);
                        }
                        result = true;
                        break;

                    case "delete_session":
                        if (pkg?.StorageService != null)
                        {
                            string dSid = payload.TryGetProperty("sessionId", out var dsp) ? dsp.GetString() : (payload.TryGetProperty("id", out var dip) ? dip.GetString() : "");
                            await pkg.StorageService.DeleteSessionAsync(dSid);
                        }
                        result = true;
                        break;

                    case "cancel_prompt":
                        if (pkg?.AcpClient != null)
                        {
                            string cSid = payload.TryGetProperty("sessionId", out var cp) ? cp.GetString() : null;
                            await pkg.AcpClient.CancelPromptAsync(cSid);
                        }
                        result = true;
                        break;

                    case "restart_gemini_session":
                        if (pkg?.AcpClient != null)
                        {
                            string restartDir = pkg.IdeContextService != null ? await pkg.IdeContextService.GetSolutionDirectoryAsync() : null;
                            await pkg.AcpClient.RestartAsync(restartDir);
                        }
                        result = true;
                        break;

                    case "search_history":
                        string sQuery = payload.GetProperty("query").GetString();
                        result = pkg?.StorageService != null
                            ? await pkg.StorageService.SearchHistoryAsync(sQuery)
                            : new System.Collections.Generic.List<Services.MessageRecord>();
                        break;

                    case "get_prompts":
                        result = pkg?.StorageService != null
                            ? await pkg.StorageService.GetPromptTemplatesAsync()
                            : new System.Collections.Generic.List<Services.PromptTemplateRecord>();
                        break;

                    case "export_session":
                        string expSid = payload.GetProperty("sessionId").GetString();
                        result = pkg?.StorageService != null
                            ? await pkg.StorageService.ExportSessionMarkdownAsync(expSid)
                            : "";
                        break;

                    case "list_workspace_files":
                        result = pkg?.IdeContextService != null
                            ? await pkg.IdeContextService.ListSolutionFilesAsync()
                            : new string[0];
                        break;

                    case "check_app_update":
                        result = new
                        {
                            update_available = false,
                            current_version = "1.0.0",
                            latest_version = "1.0.0",
                            release_notes = ""
                        };
                        break;

                    case "get_active_document_context":
                        result = pkg?.IdeContextService != null
                            ? await pkg.IdeContextService.GetActiveDocumentContextAsync()
                            : null;
                        break;

                    case "get_build_diagnostics":
                        result = pkg?.DiagnosticsService != null
                            ? await pkg.DiagnosticsService.GetActiveDiagnosticsAsync()
                            : null;
                        break;

                    case "show_native_diff":
                        if (pkg?.NativeDiffService != null)
                        {
                            string origPath = payload.GetProperty("originalPath").GetString();
                            string proposed = payload.GetProperty("proposedContent").GetString();
                            string diffTitle = payload.TryGetProperty("title", out var dt) ? dt.GetString() : "Review Agent Changes";
                            await pkg.NativeDiffService.OpenSideBySideDiffAsync(origPath, proposed, diffTitle);
                        }
                        result = true;
                        break;

                    default:
                        result = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            SendResponse(callbackId, result, errorMessage);
        }

        public void DispatchEvent(string eventName, object payload)
        {
            if (webView?.CoreWebView2 == null) return;

            try
            {
                string json = JsonSerializer.Serialize(new
                {
                    type = "event",
                    @event = eventName,
                    payload = payload
                });

                if (webView.Dispatcher.CheckAccess())
                {
                    webView.CoreWebView2.PostWebMessageAsString(json);
                }
                else
                {
                    webView.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            webView.CoreWebView2?.PostWebMessageAsString(json);
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        private void SendResponse(string callbackId, object result, string error)
        {
            if (webView?.CoreWebView2 == null) return;

            try
            {
                string json = JsonSerializer.Serialize(new
                {
                    type = "response",
                    callbackId = callbackId,
                    result = result,
                    error = error
                });

                if (webView.Dispatcher.CheckAccess())
                {
                    webView.CoreWebView2.PostWebMessageAsString(json);
                }
                else
                {
                    webView.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            webView.CoreWebView2?.PostWebMessageAsString(json);
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }
    }
}
