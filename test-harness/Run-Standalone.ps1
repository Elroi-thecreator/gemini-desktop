# Gemini Desktop - Extension Standalone Test Harness
# Runs the Visual Studio Extension WebView2 UI & Bridge in an isolated native desktop window without Visual Studio.

param (
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Gemini Desktop - Standalone Extension Test Harness      " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Loading assemblies from: $scriptDir" -ForegroundColor Gray

# Ensure WebView2Loader.dll is in the directory
$nativeLoader = Join-Path $scriptDir "runtimes\win-x64\native\WebView2Loader.dll"
$loaderTarget = Join-Path $scriptDir "WebView2Loader.dll"
if ((Test-Path $nativeLoader) -and -not (Test-Path $loaderTarget)) {
    Copy-Item $nativeLoader $loaderTarget -Force
}

# Load WPF and WebView2 Assemblies
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

Add-Type -Path (Join-Path $scriptDir "Microsoft.Web.WebView2.Core.dll")
Add-Type -Path (Join-Path $scriptDir "Microsoft.Web.WebView2.Wpf.dll")

# Setup WebDist folder
$webDistPath = Join-Path $scriptDir "Resources\WebDist"
if (-not (Test-Path (Join-Path $webDistPath "index.html"))) {
    Write-Error "Could not find index.html at $webDistPath"
    exit 1
}

# Setup user data directory
$userDataDir = Join-Path $env:LOCALAPPDATA "GeminiVS\TestHarnessData"
if (-not (Test-Path $userDataDir)) {
    New-Item -ItemType Directory -Path $userDataDir -Force | Out-Null
}

# Create WPF Window
$window = New-Object System.Windows.Window
$window.Title = "Gemini Desktop - Extension Standalone Test Window (No Visual Studio Required)"
$window.Width = 1000
$window.Height = 750
$window.WindowStartupLocation = [System.Windows.WindowStartupLocation]::CenterScreen
$window.Background = [System.Windows.Media.Brushes]::Black

$grid = New-Object System.Windows.Controls.Grid
$window.Content = $grid

# Create WebView2 Control
$webView = New-Object Microsoft.Web.WebView2.Wpf.WebView2
$grid.Children.Add($webView) | Out-Null

# In-memory mock session store
$script:sessions = [System.Collections.Generic.List[PSObject]]::new()
$script:activeSessionId = "vs-session-standalone"
$script:sessions.Add([PSCustomObject]@{
    id = "vs-session-standalone"
    workspace_id = "vs-active-solution"
    title = "Standalone Test Chat"
    created_at = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    updated_at = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
})

# Helper to send bridge response
function Send-BridgeResponse($callbackId, $result, $errorMessage = $null) {
    if (-not $webView.CoreWebView2) { return }
    $resp = [PSCustomObject]@{
        type = "response"
        callbackId = $callbackId
        result = $result
        error = $errorMessage
    } | ConvertTo-Json -Compress -Depth 10

    $webView.Dispatcher.BeginInvoke([Action]{
        try {
            $webView.CoreWebView2.PostWebMessageAsString($resp)
        } catch { }
    }) | Out-Null
}

# Helper to dispatch bridge events
function Send-BridgeEvent($eventName, $payload) {
    if (-not $webView.CoreWebView2) { return }
    $evt = [PSCustomObject]@{
        type = "event"
        event = $eventName
        payload = $payload
    } | ConvertTo-Json -Compress -Depth 10

    $webView.Dispatcher.BeginInvoke([Action]{
        try {
            $webView.CoreWebView2.PostWebMessageAsString($evt)
        } catch { }
    }) | Out-Null
}

# Shim script matching GeminiToolWindowControl.xaml.cs
$shimScript = @"
(function() {
    window.__GEMINI_VS_NATIVE__ = true;
    window.__TAURI_INTERNALS__ = {
        invoke: function(cmd, args) {
            return new Promise(function(resolve, reject) {
                var callbackId = 'cb_' + Math.random().toString(36).substring(2, 11);
                window.__gemini_callbacks = window.__gemini_callbacks || {};
                window.__gemini_callbacks[callbackId] = { resolve: resolve, reject: reject };
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'invoke',
                    command: cmd,
                    payload: args || {},
                    callbackId: callbackId
                }));
            });
        }
    };

    window.__gemini_listeners = window.__gemini_listeners || {};
    window.__TAURI_EVENT_LISTEN__ = function(event, handler) {
        window.__gemini_listeners[event] = window.__gemini_listeners[event] || [];
        window.__gemini_listeners[event].push(handler);
        return function() {
            var idx = window.__gemini_listeners[event].indexOf(handler);
            if (idx >= 0) window.__gemini_listeners[event].splice(idx, 1);
        };
    };

    if (window.location && window.location.pathname.endsWith('/index.html')) {
        try {
            window.history.replaceState(null, '', window.location.pathname.replace(/\/index\.html$/, '') || '/');
        } catch (e) {}
    }

    var handleIncomingMessage = function(e) {
        var msg = e.data;
        if (typeof msg === 'string') {
            try { msg = JSON.parse(msg); } catch (err) {}
        }
        if (!msg) return;
        if (msg.type === 'response' && msg.callbackId && window.__gemini_callbacks && window.__gemini_callbacks[msg.callbackId]) {
            if (msg.error) {
                window.__gemini_callbacks[msg.callbackId].reject(new Error(msg.error));
            } else {
                window.__gemini_callbacks[msg.callbackId].resolve(msg.result);
            }
            delete window.__gemini_callbacks[msg.callbackId];
        } else if (msg.type === 'event' && msg.event && window.__gemini_listeners && window.__gemini_listeners[msg.event]) {
            window.__gemini_listeners[msg.event].forEach(function(fn) {
                try { fn({ event: msg.event, payload: msg.payload }); } catch (err) {}
            });
        }
    };

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', handleIncomingMessage);
    }
    window.addEventListener('message', handleIncomingMessage);
})();
"@

# Initialize WebView2 when window is ready
$window.Add_Loaded({
    [System.Threading.Tasks.Task]::Run([Action]{
        $env = [Microsoft.Web.WebView2.Core.CoreWebView2Environment]::CreateAsync($null, $userDataDir).GetAwaiter().GetResult()
        
        $window.Dispatcher.Invoke([Action]{
            $webView.EnsureCoreWebView2Async($env).GetAwaiter().GetResult()
            
            $webView.CoreWebView2.Settings.AreDevToolsEnabled = $true
            $webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = $true

            # Map virtual host name directly to WebDist folder
            $webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "gemini-desktop.local",
                $webDistPath,
                [Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind]::Allow
            )

            # Inject shim script before document creation
            $webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($shimScript).GetAwaiter().GetResult()

            # Attach WebMessageReceived listener (matching hardened void OnWebMessageReceived in WebView2Bridge.cs)
            $webView.CoreWebView2.add_WebMessageReceived([EventHandler[Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs]]{
                param($sender, $e)

                $raw = $null
                try { $raw = $e.TryGetWebMessageAsString() } catch { }
                if ([string]::IsNullOrWhiteSpace($raw)) {
                    try { $raw = $e.WebMessageAsJson } catch { }
                }
                if ([string]::IsNullOrWhiteSpace($raw)) { return }

                # Decouple immediately to background task so native COM pump never stalls
                [System.Threading.Tasks.Task]::Run([Action]{
                    try {
                        $parsed = $raw | ConvertFrom-Json
                        if ($parsed.type -eq "invoke") {
                            $cmd = $parsed.command
                            $cbId = $parsed.callbackId
                            $p = $parsed.payload

                            Write-Host "[Bridge Invoke] Command: $cmd" -ForegroundColor Green

                            switch ($cmd) {
                                "check_gemini_env" {
                                    $res = [PSCustomObject]@{
                                        installed = $true
                                        cli_path = "gemini (mock/system)"
                                        cli_version = "v0.2.x"
                                        node_version = "v20.x"
                                        auth_method = "oauth"
                                        user_email = "developer@standalone.local"
                                        is_logged_in = $true
                                        error_message = $null
                                    }
                                    Send-BridgeResponse $cbId $res
                                }

                                "get_workspaces" {
                                    $res = @(
                                        [PSCustomObject]@{
                                            id = "vs-active-solution"
                                            name = "GeminiDesktop (Standalone Harness)"
                                            path = (Get-Location).Path
                                            default_model = "gemini-3.8-flash"
                                            approval_mode = "default"
                                            is_active = $true
                                        }
                                    )
                                    Send-BridgeResponse $cbId $res
                                }

                                "get_sessions" {
                                    Send-BridgeResponse $cbId $script:sessions
                                }

                                "create_session" {
                                    $newId = "session-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
                                    $newSession = [PSCustomObject]@{
                                        id = $newId
                                        workspace_id = "vs-active-solution"
                                        title = if ($p.title) { $p.title } else { "New Standalone Chat" }
                                        created_at = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
                                        updated_at = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
                                    }
                                    $script:sessions.Add($newSession)
                                    Send-BridgeResponse $cbId $newSession
                                }

                                "get_session_messages" {
                                    Send-BridgeResponse $cbId @()
                                }

                                "save_message" {
                                    Send-BridgeResponse $cbId $true
                                }

                                "get_prompts" {
                                    $res = @(
                                        [PSCustomObject]@{ id = "1"; title = "Explain Code"; content = "Explain this code in detail."; category = "Analysis" },
                                        [PSCustomObject]@{ id = "2"; title = "Optimize Performance"; content = "Review and optimize bottlenecks in this logic."; category = "Refactor" }
                                    )
                                    Send-BridgeResponse $cbId $res
                                }

                                "list_workspace_files" {
                                    $files = Get-ChildItem -Path (Get-Location) -Depth 2 -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object {
                                        [PSCustomObject]@{
                                            name = $_.Name
                                            path = $_.FullName
                                            is_dir = $_.PSIsContainer
                                            size = if ($_.PSIsContainer) { 0 } else { $_.Length }
                                            relative_path = $_.FullName.Replace((Get-Location).Path, "").TrimStart("\")
                                        }
                                    }
                                    Send-BridgeResponse $cbId $files
                                }

                                "check_app_update" {
                                    $res = [PSCustomObject]@{
                                        update_available = $false
                                        current_version = "1.0.0"
                                        latest_version = "1.0.0"
                                        release_notes = ""
                                    }
                                    Send-BridgeResponse $cbId $res
                                }

                                "get_pending_context_action" {
                                    Send-BridgeResponse $cbId $null
                                }

                                "send_prompt" {
                                    $sid = $p.sessionId
                                    $prompt = $p.prompt
                                    $model = if ($p.model) { $p.model } else { "gemini-3.8-flash" }

                                    Write-Host "[Chat Prompt Received] Session: $sid | Model: $model" -ForegroundColor Cyan
                                    Write-Host "Prompt: $prompt" -ForegroundColor White
                                    Send-BridgeResponse $cbId $true

                                    # Simulate streaming response chunks to demonstrate live ACP streaming
                                    [System.Threading.Tasks.Task]::Run([Action]{
                                        [System.Threading.Thread]::Sleep(300)
                                        $mockReply = "Hello! I am **Gemini** running inside the standalone Visual Studio Extension test harness.\n\n" +
                                                     "You clicked and submitted a message without Visual Studio open! The non-blocking `Task.Run` bridge handled your invoke call smoothly with zero crashes.\n\n" +
                                                     "- **Selected Model:** `$model`\n" +
                                                     "- **Session ID:** `$sid`\n" +
                                                     "- **Bridge Latency:** `<1ms` offload\n\n" +
                                                     "Everything is working perfectly!"

                                        $chunks = $mockReply -split " "
                                        for ($i = 0; $i -lt $chunks.Length; $i++) {
                                            $chunk = $chunks[$i] + " "
                                            $isDone = ($i -eq ($chunks.Length - 1))
                                            Send-BridgeEvent "acp-chunk" ([PSCustomObject]@{
                                                session_id = $sid
                                                delta = $chunk
                                                is_done = $isDone
                                            })
                                            [System.Threading.Thread]::Sleep(40)
                                        }
                                    })
                                }

                                default {
                                    Send-BridgeResponse $cbId $true
                                }
                            }
                        }
                    } catch {
                        Write-Host "[Bridge Error] $_" -ForegroundColor Red
                    }
                })
            })

            Write-Host "Navigating WebView2 to https://gemini-desktop.local/index.html..." -ForegroundColor Cyan
            $webView.CoreWebView2.Navigate("https://gemini-desktop.local/index.html")
        })
    })
})

Write-Host "Opening Standalone Window... (Interact with the UI, test clicking buttons, chats, etc.)" -ForegroundColor Yellow
$app = New-Object System.Windows.Application
$app.Run($window) | Out-Null
Write-Host "Standalone Test Harness Closed." -ForegroundColor Gray
