<script lang="ts">
  import type { Workspace, Session, Message, ToolPermissionPayload } from "$lib/types";
  import { renderMarkdown } from "$lib/markdown";
  import {
    Send,
    Square,
    Download,
    Terminal,
    Sparkles,
    User,
    CheckCircle,
    XCircle,
    FileText,
    Copy,
    Check,
  } from "lucide-svelte";
  import { tick } from "svelte";

  let {
    workspace,
    session,
    messages = [],
    isStreaming = false,
    streamingText = "",
    toolPermission = null,
    onSendPrompt,
    onCancelPrompt,
    onToolResponse,
    onExport,
  }: {
    workspace: Workspace | null;
    session: Session | null;
    messages: Message[];
    isStreaming: boolean;
    streamingText: string;
    toolPermission: ToolPermissionPayload | null;
    onSendPrompt: (prompt: string) => void;
    onCancelPrompt: () => void;
    onToolResponse: (requestId: number, allowed: boolean) => void;
    onExport: (format: string) => void;
  } = $props();

  let inputPrompt = $state("");
  let chatViewport: HTMLDivElement | null = $state(null);
  let textareaElem: HTMLTextAreaElement | null = $state(null);
  let showExportMenu = $state(false);

  function scrollToBottom() {
    if (chatViewport) {
      chatViewport.scrollTop = chatViewport.scrollHeight;
    }
  }

  $effect(() => {
    // Scroll whenever messages update or streaming text changes
    if (messages || streamingText) {
      tick().then(scrollToBottom);
    }
  });

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    } else if (e.key === "Escape" && isStreaming) {
      e.preventDefault();
      onCancelPrompt();
    }
  }

  function handleSubmit() {
    const trimmed = inputPrompt.trim();
    if (!trimmed || isStreaming) return;
    inputPrompt = "";
    onSendPrompt(trimmed);
  }
</script>

<div class="flex-1 h-screen flex flex-col bg-slate-950 text-slate-100 overflow-hidden">
  <!-- Top Bar -->
  <header class="h-14 px-6 border-b border-slate-800 flex items-center justify-between bg-slate-900/50 backdrop-blur-sm">
    <div class="flex items-center gap-3 truncate">
      <h2 class="font-medium text-sm text-slate-200 truncate">
        {session?.title || "Select or start a new session"}
      </h2>
      {#if workspace}
        <span class="text-[11px] px-2 py-0.5 rounded-full bg-slate-800 text-sky-400 font-mono border border-slate-700/50">
          {workspace.name} &bull; {workspace.model}
        </span>
      {/if}
    </div>

    <!-- Export Menu -->
    <div class="relative">
      <button
        onclick={() => (showExportMenu = !showExportMenu)}
        class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-slate-300 hover:text-white rounded-lg bg-slate-800 hover:bg-slate-700/80 transition-colors border border-slate-700"
      >
        <Download size={14} />
        <span>Export</span>
      </button>

      {#if showExportMenu}
        <div class="absolute right-0 mt-1.5 w-40 bg-slate-800 border border-slate-700 rounded-lg shadow-xl py-1 z-30 text-xs">
          <button
            onclick={() => {
              showExportMenu = false;
              onExport("md");
            }}
            class="w-full px-3 py-1.5 text-left hover:bg-slate-700 text-slate-200 flex items-center gap-2"
          >
            <FileText size={14} class="text-sky-400" />
            <span>Markdown (.md)</span>
          </button>
          <button
            onclick={() => {
              showExportMenu = false;
              onExport("txt");
            }}
            class="w-full px-3 py-1.5 text-left hover:bg-slate-700 text-slate-200 flex items-center gap-2"
          >
            <FileText size={14} class="text-slate-400" />
            <span>Plaintext (.txt)</span>
          </button>
          <button
            onclick={() => {
              showExportMenu = false;
              onExport("json");
            }}
            class="w-full px-3 py-1.5 text-left hover:bg-slate-700 text-slate-200 flex items-center gap-2"
          >
            <FileText size={14} class="text-emerald-400" />
            <span>Raw JSON</span>
          </button>
        </div>
      {/if}
    </div>
  </header>

  <!-- Messages Viewport -->
  <div bind:this={chatViewport} class="flex-1 overflow-y-auto p-6 space-y-6">
    {#if messages.length === 0 && !isStreaming}
      <div class="h-full flex flex-col items-center justify-center text-center max-w-md mx-auto py-20 text-slate-400">
        <div class="w-12 h-12 rounded-2xl bg-sky-500/10 border border-sky-500/20 flex items-center justify-center text-sky-400 mb-4 shadow-inner">
          <Sparkles size={24} />
        </div>
        <h3 class="text-base font-semibold text-slate-200 mb-1">What would you like to build?</h3>
        <p class="text-xs text-slate-400 leading-relaxed mb-6">
          Start a conversation in the <span class="text-sky-400 font-medium">{workspace?.name}</span> workspace. Gemini CLI will automatically use this directory's files and context.
        </p>
      </div>
    {/if}

    {#each messages as msg (msg.id)}
      <div class="flex gap-3.5 {msg.role === 'user' ? 'justify-end' : 'justify-start'}">
        {#if msg.role !== 'user'}
          <div class="w-7 h-7 rounded-lg bg-sky-600/20 text-sky-400 border border-sky-500/30 flex items-center justify-center shrink-0 mt-1">
            <Sparkles size={14} />
          </div>
        {/if}

        <div class="max-w-3xl {msg.role === 'user' ? 'bg-sky-600 text-white rounded-2xl rounded-tr-sm px-4 py-2.5 text-xs shadow-md' : 'bg-slate-900 border border-slate-800 rounded-2xl rounded-tl-sm px-5 py-4 text-xs text-slate-200 shadow-sm w-full'}">
          {#if msg.role === 'user'}
            <div class="whitespace-pre-wrap leading-relaxed select-text">{msg.content}</div>
          {:else}
            <div class="markdown-body">
              {@html renderMarkdown(msg.content)}
            </div>
          {/if}
        </div>

        {#if msg.role === 'user'}
          <div class="w-7 h-7 rounded-lg bg-slate-800 text-slate-300 flex items-center justify-center shrink-0 mt-1">
            <User size={14} />
          </div>
        {/if}
      </div>
    {/each}

    <!-- Live Streaming Response -->
    {#if isStreaming}
      <div class="flex gap-3.5 justify-start">
        <div class="w-7 h-7 rounded-lg bg-sky-600/20 text-sky-400 border border-sky-500/30 flex items-center justify-center shrink-0 mt-1 animate-pulse">
          <Sparkles size={14} />
        </div>
        <div class="max-w-3xl bg-slate-900 border border-slate-800 rounded-2xl rounded-tl-sm px-5 py-4 text-xs text-slate-200 shadow-sm w-full">
          {#if streamingText}
            <div class="markdown-body">
              {@html renderMarkdown(streamingText)}
            </div>
          {:else}
            <div class="flex items-center gap-2 text-slate-400 italic">
              <span class="inline-block w-2 h-2 rounded-full bg-sky-400 animate-ping"></span>
              Thinking...
            </div>
          {/if}
        </div>
      </div>
    {/if}
  </div>

  <!-- Tool Permission Confirmation Banner (ACP) -->
  {#if toolPermission}
    <div class="mx-6 mb-3 p-3.5 bg-amber-950/40 border border-amber-500/30 rounded-xl flex items-center justify-between shadow-lg">
      <div class="flex items-center gap-2.5">
        <div class="p-1.5 rounded-lg bg-amber-500/20 text-amber-400">
          <Terminal size={16} />
        </div>
        <div class="text-xs">
          <div class="font-semibold text-amber-300">
            Permission Request: <span class="font-mono text-white">{toolPermission.tool_name}</span>
          </div>
          <div class="text-[11px] text-amber-200/80">
            {toolPermission.reason || "Gemini CLI requests confirmation to execute this action."}
          </div>
        </div>
      </div>

      <div class="flex items-center gap-2">
        <button
          onclick={() => onToolResponse(toolPermission.request_id, true)}
          class="px-3 py-1.5 rounded-lg bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-semibold text-xs flex items-center gap-1.5 transition-colors"
        >
          <CheckCircle size={14} />
          <span>Allow</span>
        </button>
        <button
          onclick={() => onToolResponse(toolPermission.request_id, false)}
          class="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 font-semibold text-xs flex items-center gap-1.5 transition-colors"
        >
          <XCircle size={14} />
          <span>Deny</span>
        </button>
      </div>
    </div>
  {/if}

  <!-- Prompt Input Bar -->
  <div class="p-4 bg-slate-900 border-t border-slate-800">
    <div class="max-w-4xl mx-auto relative rounded-xl border border-slate-700/80 bg-slate-950/80 focus-within:border-sky-500 transition-colors shadow-inner">
      <textarea
        bind:this={textareaElem}
        bind:value={inputPrompt}
        onkeydown={handleKeyDown}
        placeholder="Type your prompt here... (Shift+Enter for newline, Enter to send)"
        rows="3"
        class="w-full px-3.5 py-2.5 bg-transparent text-slate-100 placeholder-slate-500 text-xs focus:outline-none resize-none font-sans select-text"
      ></textarea>

      <div class="flex items-center justify-between px-3 py-2 border-t border-slate-800/60 text-[11px] text-slate-500">
        <div class="flex items-center gap-2">
          <span>Working Dir: <span class="text-slate-400 font-mono">{workspace?.path || "C:\\"}</span></span>
        </div>

        <div class="flex items-center gap-2">
          {#if isStreaming}
            <button
              onclick={onCancelPrompt}
              class="flex items-center gap-1.5 px-3 py-1 rounded-lg bg-rose-500 hover:bg-rose-400 text-white font-medium text-xs transition-colors"
            >
              <Square size={12} />
              <span>Stop</span>
              <span class="text-[10px] opacity-75 font-mono">Esc</span>
            </button>
          {:else}
            <button
              onclick={handleSubmit}
              disabled={!inputPrompt.trim()}
              class="flex items-center gap-1.5 px-3 py-1 rounded-lg bg-sky-500 hover:bg-sky-400 disabled:opacity-40 disabled:hover:bg-sky-500 text-slate-950 font-semibold text-xs transition-colors"
            >
              <Send size={12} />
              <span>Send</span>
              <span class="text-[10px] opacity-75 font-mono">Enter</span>
            </button>
          {/if}
        </div>
      </div>
    </div>
  </div>
</div>
