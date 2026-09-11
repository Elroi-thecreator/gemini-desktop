<script lang="ts">
  import { invoke } from "@tauri-apps/api/core";
  import type { Workspace, TerminalCommandResult } from "$lib/types";
  import {
    Terminal,
    X,
    Play,
    Trash2,
    RefreshCw,
    CheckCircle2,
    XCircle,
    FileText,
    GitBranch,
    Folder,
    Maximize2,
    Minimize2,
    Info,
  } from "lucide-svelte";
  import { dialogManager } from "$lib/dialog.svelte";
  import Tooltip from "$lib/components/Tooltip.svelte";

  let {
    isOpen = $bindable(false),
    workspace = null,
  }: {
    isOpen: boolean;
    workspace: Workspace | null;
  } = $props();

  interface LogEntry {
    id: string;
    command: string;
    stdout: string;
    stderr: string;
    exitCode: number;
    durationMs: number;
    timestamp: string;
  }

  let commandInput = $state("");
  let isExecuting = $state(false);
  let isReloading = $state(false);
  let logs = $state<LogEntry[]>([]);
  let history = $state<string[]>([]);
  let historyIndex = $state(-1);
  let isExpanded = $state(false);
  let terminalContainer: HTMLDivElement | null = $state(null);
  let inputEl: HTMLInputElement | null = $state(null);

  $effect(() => {
    if (isOpen) {
      setTimeout(() => inputEl?.focus(), 50);
    }
  });

  $effect(() => {
    // Scroll to bottom when new logs arrive
    if (logs.length && terminalContainer) {
      setTimeout(() => {
        if (terminalContainer) {
          terminalContainer.scrollTop = terminalContainer.scrollHeight;
        }
      }, 50);
    }
  });

  async function executeCommand(cmdToRun?: string) {
    const raw = (cmdToRun !== undefined ? cmdToRun : commandInput).trim();
    if (!raw || isExecuting) return;

    if (raw === "clear" || raw === "cls") {
      logs = [];
      commandInput = "";
      return;
    }

    if (!history.length || history[history.length - 1] !== raw) {
      history = [...history, raw];
    }
    historyIndex = -1;

    commandInput = "";
    isExecuting = true;

    try {
      const res = await invoke<TerminalCommandResult>("run_terminal_command", {
        command: raw,
        workspacePath: workspace?.path || null,
      });

      logs = [
        ...logs,
        {
          id: "log-" + Date.now(),
          command: raw,
          stdout: res.stdout,
          stderr: res.stderr,
          exitCode: res.exit_code,
          durationMs: res.duration_ms,
          timestamp: new Date().toLocaleTimeString(),
        },
      ];
    } catch (e: any) {
      logs = [
        ...logs,
        {
          id: "log-" + Date.now(),
          command: raw,
          stdout: "",
          stderr: String(e),
          exitCode: 1,
          durationMs: 0,
          timestamp: new Date().toLocaleTimeString(),
        },
      ];
    } finally {
      isExecuting = false;
      setTimeout(() => inputEl?.focus(), 50);
    }
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Enter") {
      e.preventDefault();
      executeCommand();
    } else if (e.key === "ArrowUp") {
      if (!history.length) return;
      e.preventDefault();
      if (historyIndex === -1) {
        historyIndex = history.length - 1;
      } else if (historyIndex > 0) {
        historyIndex--;
      }
      commandInput = history[historyIndex];
    } else if (e.key === "ArrowDown") {
      if (historyIndex === -1) return;
      e.preventDefault();
      if (historyIndex < history.length - 1) {
        historyIndex++;
        commandInput = history[historyIndex];
      } else {
        historyIndex = -1;
        commandInput = "";
      }
    }
  }

  async function handleReloadGemini() {
    if (isReloading) return;
    isReloading = true;
    try {
      const msg = await invoke<string>("restart_gemini_session");
      logs = [
        ...logs,
        {
          id: "reload-" + Date.now(),
          command: "[SYSTEM] Reload Gemini CLI Session",
          stdout: `✓ ${msg}\nAny updated .env or environment variables in "${workspace?.path || "workspace"}" will take effect on the next prompt.`,
          stderr: "",
          exitCode: 0,
          durationMs: 0,
          timestamp: new Date().toLocaleTimeString(),
        },
      ];
      await dialogManager.alert(
        "Gemini CLI session reset successfully!\n\nAll variables from your workspace .env file will be automatically injected on your next prompt.",
        "Environment Reloaded"
      );
    } catch (e: any) {
      logs = [
        ...logs,
        {
          id: "reload-err-" + Date.now(),
          command: "[SYSTEM] Reload Gemini CLI Session",
          stdout: "",
          stderr: `Failed to reload session: ${e}`,
          exitCode: 1,
          durationMs: 0,
          timestamp: new Date().toLocaleTimeString(),
        },
      ];
    } finally {
      isReloading = false;
    }
  }
</script>

{#if isOpen}
  <div
    class="border-t border-theme-default bg-surface/95 backdrop-blur-md flex flex-col transition-all duration-200 z-40 {isExpanded ? 'h-[65vh]' : 'h-72'}"
  >
    <!-- Header Controls -->
    <div class="px-4 py-2 border-b border-subtle flex items-center justify-between bg-surface-elevated/40 text-xs select-none">
      <div class="flex items-center gap-2 truncate">
        <div class="p-1 rounded bg-accent-subtle text-accent-theme flex items-center justify-center">
          <Terminal size={14} />
        </div>
        <span class="font-semibold text-primary-theme">Terminal Console</span>
        <Tooltip text="Interactive PowerShell Terminal" subtext="Executes commands inside the active workspace directory. Adding variables to .env and clicking 'Reload Gemini' updates your AI session." position="bottom">
          <div class="p-0.5 text-muted-theme hover:text-accent-theme cursor-help">
            <Info size={13} />
          </div>
        </Tooltip>
        <span class="text-[10px] px-1.5 py-0.5 rounded bg-surface border border-subtle text-muted-theme font-mono truncate max-w-[240px]">
          {workspace?.path || "C:\\"}
        </span>
        {#if isExecuting}
          <span class="text-[10px] text-accent-theme flex items-center gap-1 animate-pulse">
            <RefreshCw size={11} class="animate-spin" />
            Executing...
          </span>
        {/if}
      </div>

      <!-- Quick Action Buttons -->
      <div class="flex items-center gap-1.5">
        <Tooltip text="Git Status" subtext="Inspect modified files, staged changes, and current branch in this workspace" position="bottom">
          <button
            type="button"
            onclick={() => executeCommand("git status")}
            class="px-2 py-1 rounded text-[11px] text-secondary-theme hover:text-primary-theme hover:bg-surface border border-transparent hover:border-subtle flex items-center gap-1 transition-colors cursor-pointer"
          >
            <GitBranch size={12} />
            <span>git status</span>
          </button>
        </Tooltip>

        <Tooltip text="List Directory (dir / ls)" subtext="Lists all files and folders located in the root of the workspace directory" position="bottom">
          <button
            type="button"
            onclick={() => executeCommand(navigator.userAgent.includes("Windows") ? "dir" : "ls -la")}
            class="px-2 py-1 rounded text-[11px] text-secondary-theme hover:text-primary-theme hover:bg-surface border border-transparent hover:border-subtle flex items-center gap-1 transition-colors cursor-pointer"
          >
            <Folder size={12} />
            <span>dir</span>
          </button>
        </Tooltip>

        <Tooltip text="View .env File (cat .env)" subtext="Reads and displays your workspace .env file to inspect environment variables and secrets" position="bottom">
          <button
            type="button"
            onclick={() => executeCommand(navigator.userAgent.includes("Windows") ? "Get-Content .env -ErrorAction SilentlyContinue" : "cat .env")}
            class="px-2 py-1 rounded text-[11px] text-secondary-theme hover:text-primary-theme hover:bg-surface border border-transparent hover:border-subtle flex items-center gap-1 transition-colors cursor-pointer"
          >
            <FileText size={12} />
            <span>View .env</span>
          </button>
        </Tooltip>

        <div class="h-3.5 w-px bg-subtle mx-1"></div>

        <Tooltip text="Reload Gemini AI Session" subtext="Restarts the background Gemini CLI process to apply newly added .env or system environment variables immediately" position="bottom">
          <button
            type="button"
            onclick={handleReloadGemini}
            disabled={isReloading}
            class="px-2.5 py-1 rounded text-[11px] bg-accent-subtle hover:bg-accent-theme hover:text-white text-accent-theme font-medium flex items-center gap-1 transition-colors cursor-pointer disabled:opacity-50"
          >
            <RefreshCw size={12} class={isReloading ? "animate-spin" : ""} />
            <span>Reload Gemini</span>
          </button>
        </Tooltip>

        <Tooltip text="Clear Console" subtext="Wipes all command history and logs from the terminal screen (cls)" position="bottom">
          <button
            type="button"
            onclick={() => (logs = [])}
            class="p-1.5 text-secondary-theme hover:text-primary-theme hover:bg-surface rounded transition-colors cursor-pointer"
          >
            <Trash2 size={13} />
          </button>
        </Tooltip>

        <Tooltip text={isExpanded ? "Collapse Height" : "Expand Height"} subtext="Toggle between compact drawer and tall console" position="bottom">
          <button
            type="button"
            onclick={() => (isExpanded = !isExpanded)}
            class="p-1.5 text-secondary-theme hover:text-primary-theme hover:bg-surface rounded transition-colors cursor-pointer"
          >
            {#if isExpanded}
              <Minimize2 size={13} />
            {:else}
              <Maximize2 size={13} />
            {/if}
          </button>
        </Tooltip>

        <Tooltip text="Close Terminal Drawer" shortcut="Ctrl+`" position="bottom">
          <button
            type="button"
            onclick={() => (isOpen = false)}
            class="p-1.5 text-secondary-theme hover:text-primary-theme hover:bg-surface rounded transition-colors cursor-pointer"
          >
            <X size={14} />
          </button>
        </Tooltip>
      </div>
    </div>

    <!-- Output Terminal Stream -->
    <div
      bind:this={terminalContainer}
      class="flex-1 p-3 overflow-y-auto font-mono text-[11px] leading-relaxed bg-[#0a0d14] text-slate-200 select-text space-y-2 border-b border-subtle"
    >
      {#if logs.length === 0}
        <div class="text-slate-500 py-1 space-y-1">
          <div><span class="text-accent-theme font-semibold">Gemini Desktop Terminal Console</span> — PowerShell environment initialized.</div>
          <div class="text-[10px]">
            • Current working directory: <code class="text-slate-400">{workspace?.path || "C:\\"}</code>
          </div>
          <div class="text-[10px]">
            • To set environment variables for Gemini CLI, add them to <code class="text-accent-theme">.env</code> (e.g. <code>echo "AZURE_DEVOPS_EXT_PAT=..." &gt;&gt; .env</code>) and click <b class="text-slate-300">Reload Gemini</b>.
          </div>
          <div class="text-[10px]">
            • Use <code class="text-slate-400">Up / Down</code> arrows for history, <code class="text-slate-400">clear</code> to wipe terminal.
          </div>
        </div>
      {:else}
        {#each logs as log}
          <div class="border-b border-slate-800/80 pb-2">
            <!-- Command line indicator -->
            <div class="flex items-center justify-between text-[10px] text-slate-400 mb-1">
              <div class="flex items-center gap-1.5 truncate">
                <span class="text-emerald-400 font-semibold">&gt;</span>
                <span class="text-sky-300 font-bold">{log.command}</span>
              </div>
              <div class="flex items-center gap-2 shrink-0">
                {#if log.durationMs > 0}
                  <span class="text-slate-500 font-mono">{log.durationMs}ms</span>
                {/if}
                {#if log.exitCode === 0}
                  <span class="text-emerald-400 flex items-center gap-0.5" title="Exit code 0">
                    <CheckCircle2 size={11} />
                  </span>
                {:else}
                  <span class="text-rose-400 flex items-center gap-0.5" title="Exit code {log.exitCode}">
                    <XCircle size={11} />
                    <span>{log.exitCode}</span>
                  </span>
                {/if}
                <span class="text-slate-600">{log.timestamp}</span>
              </div>
            </div>

            <!-- Standard Output -->
            {#if log.stdout}
              <pre class="text-slate-200 whitespace-pre-wrap break-all pl-3 font-mono">{log.stdout}</pre>
            {/if}

            <!-- Standard Error -->
            {#if log.stderr}
              <pre class="text-rose-400 whitespace-pre-wrap break-all pl-3 font-mono mt-1">{log.stderr}</pre>
            {/if}
          </div>
        {/each}
      {/if}
    </div>

    <!-- Input Prompt Bar -->
    <div class="px-3 py-2 bg-[#0d1117] flex items-center gap-2 text-xs">
      <div class="flex items-center gap-1 text-emerald-400 font-mono text-[11px] shrink-0 select-none">
        <span>PS</span>
        <span class="text-sky-400 truncate max-w-[150px]">
          {workspace?.path ? workspace.path.split(/[\\/]/).pop() || workspace.name : "gemini"}
        </span>
        <span>&gt;</span>
      </div>

      <input
        bind:this={inputEl}
        type="text"
        bind:value={commandInput}
        onkeydown={handleKeyDown}
        disabled={isExecuting}
        placeholder={isExecuting ? "Command is running..." : "Type command (e.g. echo \"KEY=VAL\" >> .env, git status, npm test)..."}
        class="flex-1 bg-transparent border-none text-slate-100 font-mono text-xs focus:outline-none placeholder:text-slate-600"
      />

      <button
        type="button"
        onclick={() => executeCommand()}
        disabled={!commandInput.trim() || isExecuting}
        class="p-1 text-slate-400 hover:text-accent-theme disabled:opacity-30 rounded transition-colors cursor-pointer"
        title="Execute command (Enter)"
      >
        <Play size={13} class={isExecuting ? "animate-spin" : ""} />
      </button>
    </div>
  </div>
{/if}
