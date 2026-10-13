using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Gemini.Extension.UI
{
    public partial class GeminiToolWindowControl : UserControl
    {
        private Bridge.WebView2Bridge bridge;
        private WebView2 WebView;
        private bool isInitialized = false;

        public GeminiToolWindowControl()
        {
            InitializeComponent();

            WebView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            WebViewHost.Children.Add(WebView);

            Loaded += OnLoaded;
            VSColorTheme.ThemeChanged += OnVsThemeChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeWebView2Async();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Gemini Desktop Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task InitializeWebView2Async()
        {
            if (isInitialized) return;
            isInitialized = true;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userDataFolder = Path.Combine(localAppData, "GeminiVS", "WebView2Data");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);

            // Locate WebDist folder from extension install directory
            string extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string webDistPath = null;

            string candidateWebDist = Path.Combine(extensionDir, "Resources", "WebDist");
            if (File.Exists(Path.Combine(candidateWebDist, "index.html")))
            {
                webDistPath = candidateWebDist;
            }
            else if (File.Exists(Path.Combine(extensionDir, "index.html")))
            {
                webDistPath = extensionDir;
            }
            else if (File.Exists(Path.Combine(extensionDir, "WebDist", "index.html")))
            {
                webDistPath = Path.Combine(extensionDir, "WebDist");
            }
            else
            {
                string devPath = Path.GetFullPath(Path.Combine(extensionDir, "..", "..", "..", "..", "build"));
                if (File.Exists(Path.Combine(devPath, "index.html")))
                {
                    webDistPath = devPath;
                }
            }

            // If not found anywhere, ensure Resources\WebDist exists and populate self-contained fallback index.html
            if (webDistPath == null)
            {
                webDistPath = candidateWebDist;
                try
                {
                    Directory.CreateDirectory(webDistPath);
                    string fallbackIndex = Path.Combine(webDistPath, "index.html");
                    if (!File.Exists(fallbackIndex))
                    {
                        File.WriteAllText(fallbackIndex, GetFallbackHtml());
                    }
                }
                catch { }
            }

            // Map virtual host name to the static assets folder
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "gemini-desktop.local",
                webDistPath,
                CoreWebView2HostResourceAccessKind.Allow
            );

            // Inject Tauri API compatibility shim so frontend invokes route smoothly to C#
            string shimScript = @"
                (function() {
                    window.__GEMINI_VS_NATIVE__ = true;
                    window.__TAURI_INTERNALS__ = {
                        invoke: function(cmd, args) {
                            return new Promise(function(resolve, reject) {
                                var callbackId = 'cb_' + Math.random().toString(36).substring(2, 11);
                                window.__gemini_callbacks = window.__gemini_callbacks || {};
                                window.__gemini_callbacks[callbackId] = { resolve: resolve, reject: reject };
                                window.chrome.webview.postMessage({
                                    type: 'invoke',
                                    command: cmd,
                                    payload: args || {},
                                    callbackId: callbackId
                                });
                            });
                        }
                    };

                    // Global event listener for ACP stream events
                    window.__gemini_listeners = window.__gemini_listeners || {};
                    window.__TAURI_EVENT_LISTEN__ = function(event, handler) {
                        window.__gemini_listeners[event] = window.__gemini_listeners[event] || [];
                        window.__gemini_listeners[event].push(handler);
                        return function() {
                            var idx = window.__gemini_listeners[event].indexOf(handler);
                            if (idx >= 0) window.__gemini_listeners[event].splice(idx, 1);
                        };
                    };

                    window.addEventListener('message', function(e) {
                        var msg = e.data;
                        if (!msg) return;
                        if (msg.type === 'response' && msg.callbackId && window.__gemini_callbacks[msg.callbackId]) {
                            if (msg.error) {
                                window.__gemini_callbacks[msg.callbackId].reject(new Error(msg.error));
                            } else {
                                window.__gemini_callbacks[msg.callbackId].resolve(msg.result);
                            }
                            delete window.__gemini_callbacks[msg.callbackId];
                        } else if (msg.type === 'event' && msg.event && window.__gemini_listeners[msg.event]) {
                            window.__gemini_listeners[msg.event].forEach(function(fn) {
                                fn({ event: msg.event, payload: msg.payload });
                            });
                        }
                    });
                })();
            ";

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(shimScript);

            // Instantiate host bridge
            bridge = new Bridge.WebView2Bridge(WebView);
            if (GeminiPackage.Instance != null)
            {
                GeminiPackage.Instance.Bridge = bridge;
            }
            WebView.CoreWebView2.WebMessageReceived += bridge.OnWebMessageReceived;

            // Send initial theme configuration
            SendThemeUpdate();

            // Navigate to SPA entrypoint
            WebView.Source = new Uri("https://gemini-desktop.local/index.html");

            WebView.NavigationCompleted += (s, args) =>
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                if (!args.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] Navigation error: {args.WebErrorStatus}");
                    try
                    {
                        WebView.CoreWebView2.NavigateToString(GetFallbackHtml());
                    }
                    catch { }
                }
            };
        }

        private void OnVsThemeChanged(ThemeChangedEventArgs e)
        {
            SendThemeUpdate();
        }

        private void SendThemeUpdate()
        {
            if (WebView?.CoreWebView2 == null) return;

            // Query VS theme colors
            var bg = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            var fg = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowTextColorKey);

            bool isDark = (bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114) < 128;

            string themeJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "event",
                @event = "vs-theme-updated",
                payload = new
                {
                    isDark = isDark,
                    bg = $"#{bg.R:X2}{bg.G:X2}{bg.B:X2}",
                    fg = $"#{fg.R:X2}{fg.G:X2}{fg.B:X2}"
                }
            });

            WebView.CoreWebView2.PostWebMessageAsString(themeJson);
        }

        private static string GetFallbackHtml()
        {
            try
            {
                string extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string htmlPath = Path.Combine(extensionDir, "Resources", "WebDist", "index.html");
                if (File.Exists(htmlPath)) return File.ReadAllText(htmlPath);

                string rootHtml = Path.Combine(extensionDir, "index.html");
                if (File.Exists(rootHtml)) return File.ReadAllText(rootHtml);
            }
            catch { }

            return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<title>Gemini Desktop</title>
<style>
body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #1e1e1e; color: #d4d4d4; padding: 24px; margin: 0; }
.card { background: #252526; border: 1px solid #3c3c3c; border-radius: 8px; padding: 24px; max-width: 500px; margin: 40px auto; text-align: center; }
h2 { color: #38bdf8; margin-top: 0; }
p { color: #9ca3af; font-size: 13px; line-height: 1.6; }
code { background: #181a1f; color: #38bdf8; padding: 2px 6px; border-radius: 4px; font-family: Consolas, monospace; }
</style>
</head>
<body>
<div class='card'>
  <h2>✨ Gemini Desktop Active</h2>
  <p>The Visual Studio 2022 extension bridge is running. You can interact with your code via editor commands or set your Gemini CLI path in <code>Tools &gt; Options &gt; Gemini &gt; General</code>.</p>
</div>
</body>
</html>";
        }
    }
}
