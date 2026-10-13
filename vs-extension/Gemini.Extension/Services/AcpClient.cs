using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gemini.Extension.Services
{
    public sealed class AcpClient : IDisposable
    {
        private readonly ProcessSupervisor supervisor;
        private Process cliProcess;
        private StreamWriter stdinWriter;
        private StreamReader stdoutReader;
        private StreamReader stderrReader;
        private CancellationTokenSource cts;
        private long requestIdCounter = 1;
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingRequests = new ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>();

        public event Action<string, string, bool> OnChunkReceived; // sessionId, delta, isDone
        public event Action<long, string, string, string> OnToolPermissionRequested; // requestId, sessionId, toolName, title
        public event Action<string> OnError;
        public event Action<string> OnStderr;

        public AcpClient(ProcessSupervisor supervisor)
        {
            this.supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        }

        public bool IsRunning => cliProcess != null && !cliProcess.HasExited;

        public async Task<bool> StartAsync(string workingDirectory = null, string approvalMode = "default")
        {
            Stop();

            cts = new CancellationTokenSource();
            string cliPath = FindGeminiCliPath();

            if (string.IsNullOrEmpty(cliPath))
            {
                OnError?.Invoke("Gemini CLI executable was not found on this machine. Please install via 'npm install -g @google/gemini-cli' or configure its path in Tools > Options > Gemini > General.");
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = $"--approval-mode {approvalMode} --skip-trust",
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                cliProcess = new Process { StartInfo = startInfo };
                if (!cliProcess.Start())
                {
                    OnError?.Invoke($"Failed to launch process for Gemini CLI at '{cliPath}'.");
                    return false;
                }

                // Supervise process with Win32 Job Object
                supervisor.AssignProcess(cliProcess);

                stdinWriter = cliProcess.StandardInput;
                stdoutReader = cliProcess.StandardOutput;
                stderrReader = cliProcess.StandardError;

                _ = Task.Run(() => ReadStdoutLoopAsync(cts.Token));
                _ = Task.Run(() => ReadStderrLoopAsync(cts.Token));

                // Perform ACP initialize handshake
                await SendRequestAsync("initialize", new
                {
                    protocolVersion = 1,
                    clientCapabilities = new { terminal = true }
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gemini Desktop] Error starting CLI at '{cliPath}': {ex}");
                OnError?.Invoke($"Failed to start Gemini CLI at '{cliPath}': {ex.Message}. Verify Gemini CLI is installed ('npm install -g @google/gemini-cli') or set its path in Tools > Options > Gemini > General.");
                Stop();
                return false;
            }
        }

        public async Task<string> CreateSessionAsync(string cwd = null)
        {
            var result = await SendRequestAsync("session/new", new
            {
                cwd = cwd ?? Environment.CurrentDirectory
            });

            if (result.TryGetProperty("sessionId", out var sidProp))
            {
                return sidProp.GetString();
            }

            return Guid.NewGuid().ToString();
        }

        public async Task SendPromptAsync(string sessionId, string prompt, string model = null)
        {
            if (!IsRunning)
            {
                bool started = await StartAsync();
                if (!started) return;
            }

            if (!string.IsNullOrEmpty(model))
            {
                await SendRequestAsync("session/set_model", new
                {
                    sessionId = sessionId,
                    modelId = model
                });
            }

            _ = SendRequestAsync("session/prompt", new
            {
                sessionId = sessionId,
                prompt = prompt
            });
        }

        public async Task CancelPromptAsync(string sessionId)
        {
            try
            {
                if (IsRunning && !string.IsNullOrEmpty(sessionId))
                {
                    _ = SendRequestAsync("session/cancel", new { sessionId = sessionId });
                }
            }
            catch { }
            finally
            {
                // Force UI stream unlock
                OnChunkReceived?.Invoke(sessionId, "", true);
            }
        }

        public async Task<bool> RestartAsync(string workingDirectory = null, string approvalMode = "default")
        {
            Stop();
            return await StartAsync(workingDirectory, approvalMode);
        }

        public async Task RespondToolPermissionAsync(long requestId, string optionId, bool allowed)
        {
            object responseObj;
            if (allowed)
            {
                responseObj = new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    result = new
                    {
                        outcome = new
                        {
                            outcome = "selected",
                            optionId = optionId ?? "allow"
                        }
                    }
                };
            }
            else
            {
                responseObj = new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    result = new
                    {
                        outcome = new
                        {
                            outcome = "cancelled"
                        }
                    }
                };
            }

            string jsonLine = JsonSerializer.Serialize(responseObj);
            await WriteStdinLineAsync(jsonLine);
        }

        private async Task<JsonElement> SendRequestAsync(string method, object @params)
        {
            long id = Interlocked.Increment(ref requestIdCounter);
            var tcs = new TaskCompletionSource<JsonElement>();
            pendingRequests[id] = tcs;

            var rpcObj = new
            {
                jsonrpc = "2.0",
                id = id,
                method = method,
                @params = @params
            };

            string line = JsonSerializer.Serialize(rpcObj);
            await WriteStdinLineAsync(line);

            return await tcs.Task;
        }

        private async Task WriteStdinLineAsync(string line)
        {
            if (stdinWriter == null) return;
            await stdinWriter.WriteLineAsync(line);
            await stdinWriter.FlushAsync();
        }

        private async Task ReadStdoutLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && stdoutReader != null && !stdoutReader.EndOfStream)
                {
                    string line = await stdoutReader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    ProcessIncomingLine(line);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"ACP stdout loop terminated: {ex.Message}");
            }
        }

        private async Task ReadStderrLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && stderrReader != null && !stderrReader.EndOfStream)
                {
                    string line = await stderrReader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        OnStderr?.Invoke(line);
                    }
                }
            }
            catch
            {
                // Silently terminate on shutdown
            }
        }

        private void ProcessIncomingLine(string line)
        {
            try
            {
                using (var doc = JsonDocument.Parse(line))
                {
                    var root = doc.RootElement;

                    // Handle JSON-RPC responses to client requests
                    if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                    {
                        long id = idProp.GetInt64();
                        if (pendingRequests.TryRemove(id, out var tcs))
                        {
                            if (root.TryGetProperty("result", out var resProp))
                            {
                                tcs.TrySetResult(resProp.Clone());
                            }
                            else if (root.TryGetProperty("error", out var errProp))
                            {
                                tcs.TrySetException(new Exception(errProp.ToString()));
                            }
                        }
                    }

                    // Handle notifications from Agent (session/update)
                    if (root.TryGetProperty("method", out var methodProp))
                    {
                        string method = methodProp.GetString();
                        if (method == "session/update" && root.TryGetProperty("params", out var paramsProp))
                        {
                            string sessionId = paramsProp.TryGetProperty("sessionId", out var sidProp) ? sidProp.GetString() : "";

                            // Handle tool permission request from agent
                            if (paramsProp.TryGetProperty("sessionUpdate", out var suProp) && suProp.GetString() == "tool_call")
                            {
                                long reqId = root.TryGetProperty("id", out var rId) ? rId.GetInt64() : 0;
                                string toolName = paramsProp.TryGetProperty("toolName", out var tn) ? tn.GetString() : "tool";
                                string title = paramsProp.TryGetProperty("title", out var tt) ? tt.GetString() : "";

                                OnToolPermissionRequested?.Invoke(reqId, sessionId, toolName, title);
                                return;
                            }

                            // Handle text delta stream
                            string delta = ExtractDeltaText(paramsProp);
                            bool isDone = paramsProp.TryGetProperty("isDone", out var doneProp) && doneProp.GetBoolean();

                            if (!string.IsNullOrEmpty(delta) || isDone)
                            {
                                OnChunkReceived?.Invoke(sessionId, delta, isDone);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Error parsing ACP line: {ex.Message}");
            }
        }

        private string ExtractDeltaText(JsonElement element)
        {
            if (element.TryGetProperty("delta", out var deltaProp))
            {
                if (deltaProp.ValueKind == JsonValueKind.String)
                {
                    return deltaProp.GetString();
                }
                if (deltaProp.ValueKind == JsonValueKind.Array)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in deltaProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("text", out var tProp))
                        {
                            sb.Append(tProp.GetString());
                        }
                    }
                    return sb.ToString();
                }
            }
            return "";
        }

        public string FindGeminiCliPath()
        {
            // 1. Check user-configured path in Tools > Options > Gemini > General
            try
            {
                var options = GeminiPackage.Instance?.Options;
                if (options != null && !string.IsNullOrWhiteSpace(options.GeminiCliPath))
                {
                    string configured = options.GeminiCliPath.Trim().Trim('"');
                    if (File.Exists(configured)) return configured;
                }
            }
            catch { }

            // 2. Dynamic system search via where.exe
            string[] whereQueries = { "gemini", "gemini.cmd", "gemini.exe" };
            foreach (var query in whereQueries)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "where.exe",
                        Arguments = query,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            string candidate = line.Trim();
                            if (File.Exists(candidate))
                            {
                                return candidate;
                            }
                        }
                        p.WaitForExit(600);
                    }
                }
                catch { }
            }

            // 3. Check system and user PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] extensions = { "gemini.cmd", "gemini.exe", "gemini.bat" };
            foreach (var dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string cleanDir = dir.Trim().Trim('"');
                foreach (var ext in extensions)
                {
                    string candidate = Path.Combine(cleanDir, ext);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // 4. Default npm global path (%APPDATA%\npm\gemini.cmd)
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string npmCmd = Path.Combine(appData, "npm", "gemini.cmd");
            if (File.Exists(npmCmd)) return npmCmd;

            // 5. Default WinGet path (%LOCALAPPDATA%\Microsoft\WinGet\Links\gemini.exe)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string wingetExe = Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "gemini.exe");
            if (File.Exists(wingetExe)) return wingetExe;

            // 6. Default Program Files Node.js paths
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string nodeCmd = Path.Combine(progFiles, "nodejs", "gemini.cmd");
            if (File.Exists(nodeCmd)) return nodeCmd;

            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string nodeCmdX86 = Path.Combine(progFilesX86, "nodejs", "gemini.cmd");
            if (File.Exists(nodeCmdX86)) return nodeCmdX86;

            // 7. LocalAppData Programs Node.js path
            string localNodeCmd = Path.Combine(localAppData, "Programs", "nodejs", "gemini.cmd");
            if (File.Exists(localNodeCmd)) return localNodeCmd;

            // 8. Yarn / pnpm global bins
            string yarnCmd = Path.Combine(localAppData, "Yarn", "bin", "gemini.cmd");
            if (File.Exists(yarnCmd)) return yarnCmd;
            string pnpmCmd = Path.Combine(localAppData, "pnpm", "gemini.cmd");
            if (File.Exists(pnpmCmd)) return pnpmCmd;

            // 9. Scoop, Volta, Chocolatey package managers
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string scoopCmd = Path.Combine(userProfile, "scoop", "shims", "gemini.cmd");
            if (File.Exists(scoopCmd)) return scoopCmd;

            string voltaCmd = Path.Combine(localAppData, "Volta", "bin", "gemini.cmd");
            if (File.Exists(voltaCmd)) return voltaCmd;

            string voltaProfile = Path.Combine(userProfile, ".volta", "bin", "gemini.cmd");
            if (File.Exists(voltaProfile)) return voltaProfile;

            string commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string chocoCmd = Path.Combine(commonAppData, "chocolatey", "bin", "gemini.exe");
            if (File.Exists(chocoCmd)) return chocoCmd;

            return null;
        }

        public void Stop()
        {
            cts?.Cancel();
            try
            {
                if (cliProcess != null && !cliProcess.HasExited)
                {
                    cliProcess.Kill();
                }
            }
            catch { }
            cliProcess?.Dispose();
            cliProcess = null;
        }

        public void Dispose()
        {
            Stop();
            cts?.Dispose();
        }
    }
}
