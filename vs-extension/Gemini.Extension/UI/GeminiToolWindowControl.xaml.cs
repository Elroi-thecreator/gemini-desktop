using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Web.WebView2.Core;

namespace Gemini.Extension.UI
{
    public partial class GeminiToolWindowControl : UserControl
    {
        private Bridge.WebView2Bridge bridge;

        public GeminiToolWindowControl()
        {
            InitializeComponent();
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
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userDataFolder = Path.Combine(localAppData, "GeminiVS", "WebView2Data");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);

            // Locate WebDist folder from extension install directory
            string extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string webDistPath = Path.Combine(extensionDir, "Resources", "WebDist");

            // Fallback for development if Resources/WebDist is not yet compiled
            if (!Directory.Exists(webDistPath))
            {
                // In dev mode, check workspace build output
                string devPath = Path.GetFullPath(Path.Combine(extensionDir, "..", "..", "..", "..", "build"));
                if (Directory.Exists(devPath))
                {
                    webDistPath = devPath;
                }
                else
                {
                    Directory.CreateDirectory(webDistPath);
                }
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
            var bg = VSColorTheme.GetThemedGDIColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            var fg = VSColorTheme.GetThemedGDIColor(EnvironmentColors.ToolWindowTextColorKey);

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
    }
}
