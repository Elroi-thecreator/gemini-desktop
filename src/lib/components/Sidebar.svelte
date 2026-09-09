<script lang="ts">
  import type { Workspace, Session } from "$lib/types";
  import {
    Plus,
    Search,
    BookOpen,
    FolderKanban,
    MessageSquare,
    Trash2,
    Edit2,
    Settings,
    Sparkles,
    ChevronDown,
  } from "lucide-svelte";

  let {
    workspaces = [],
    activeWorkspace = null,
    sessions = [],
    activeSession = null,
    onSelectWorkspace,
    onSelectSession,
    onNewSession,
    onRenameSession,
    onDeleteSession,
    onOpenSearch,
    onOpenTemplates,
    onOpenWorkspaceModal,
    envStatus = null,
  }: {
    workspaces: Workspace[];
    activeWorkspace: Workspace | null;
    sessions: Session[];
    activeSession: Session | null;
    onSelectWorkspace: (ws: Workspace) => void;
    onSelectSession: (s: Session) => void;
    onNewSession: () => void;
    onRenameSession: (s: Session) => void;
    onDeleteSession: (s: Session) => void;
    onOpenSearch: () => void;
    onOpenTemplates: () => void;
    onOpenWorkspaceModal: () => void;
    envStatus: any;
  } = $props();

  let showWorkspaceMenu = $state(false);
</script>

<aside class="w-72 h-screen flex flex-col bg-slate-900 border-r border-slate-800 select-none">
  <!-- Top App Brand -->
  <div class="p-3.5 border-b border-slate-800/80 flex items-center justify-between">
    <div class="flex items-center gap-2.5">
      <div class="w-8 h-8 rounded-lg bg-gradient-to-tr from-sky-500 to-indigo-500 flex items-center justify-center text-white shadow-md shadow-sky-500/20">
        <Sparkles size={18} />
      </div>
      <div>
        <h1 class="text-sm font-semibold tracking-tight text-white flex items-center gap-1.5">
          Gemini Desktop
        </h1>
        <div class="flex items-center gap-1.5 text-[11px] text-slate-400">
          <span class="inline-block w-1.5 h-1.5 rounded-full {envStatus?.installed ? 'bg-emerald-400' : 'bg-amber-400'}"></span>
          <span>{envStatus?.installed ? 'CLI Connected' : 'Mock / Standby'}</span>
        </div>
      </div>
    </div>
  </div>

  <!-- Workspace Selector -->
  <div class="p-3 border-b border-slate-800/60">
    <div class="text-[11px] font-semibold uppercase text-slate-400 tracking-wider mb-1.5 px-1">
      Workspace Profile
    </div>
    <div class="relative">
      <button
        onclick={() => (showWorkspaceMenu = !showWorkspaceMenu)}
        class="w-full flex items-center justify-between px-3 py-2 rounded-lg bg-slate-800/80 hover:bg-slate-800 border border-slate-700/60 text-xs text-slate-200 transition-all text-left"
      >
        <div class="flex items-center gap-2 truncate">
          <FolderKanban size={14} class="text-sky-400 shrink-0" />
          <span class="truncate font-medium">{activeWorkspace?.name || "Select Workspace"}</span>
        </div>
        <ChevronDown size={14} class="text-slate-400 shrink-0" />
      </button>

      {#if showWorkspaceMenu}
        <div class="absolute left-0 right-0 mt-1 bg-slate-800 border border-slate-700 rounded-lg shadow-xl py-1 z-30">
          {#each workspaces as ws}
            <button
              onclick={() => {
                onSelectWorkspace(ws);
                showWorkspaceMenu = false;
              }}
              class="w-full px-3 py-1.5 text-xs text-left flex items-center justify-between hover:bg-slate-700/80 transition-colors {activeWorkspace?.id === ws.id ? 'text-sky-400 font-medium' : 'text-slate-300'}"
            >
              <span class="truncate">{ws.name}</span>
              <span class="text-[10px] text-slate-400 font-mono">{ws.model}</span>
            </button>
          {/each}
          <div class="border-t border-slate-700/80 my-1"></div>
          <button
            onclick={() => {
              showWorkspaceMenu = false;
              onOpenWorkspaceModal();
            }}
            class="w-full px-3 py-1.5 text-xs text-left text-sky-400 hover:bg-slate-700/80 flex items-center gap-1.5"
          >
            <Settings size={12} />
            <span>Manage Workspaces...</span>
          </button>
        </div>
      {/if}
    </div>
  </div>

  <!-- Quick Action Navigation -->
  <div class="p-3 space-y-1 border-b border-slate-800/60">
    <button
      onclick={onNewSession}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg bg-sky-500 hover:bg-sky-400 text-slate-950 font-semibold text-xs transition-colors shadow-sm"
    >
      <Plus size={16} />
      <span>New Chat</span>
      <span class="ml-auto text-[10px] opacity-75 font-mono">Ctrl+N</span>
    </button>

    <button
      onclick={onOpenSearch}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-slate-800/80 text-slate-300 text-xs transition-colors"
    >
      <Search size={14} class="text-slate-400" />
      <span>Search History</span>
      <span class="ml-auto text-[10px] text-slate-500 font-mono">Ctrl+K</span>
    </button>

    <button
      onclick={onOpenTemplates}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-slate-800/80 text-slate-300 text-xs transition-colors"
    >
      <BookOpen size={14} class="text-slate-400" />
      <span>Prompt Library</span>
    </button>
  </div>

  <!-- Chat History Sessions List -->
  <div class="flex-1 overflow-y-auto p-2 space-y-0.5">
    <div class="px-2 py-1.5 text-[11px] font-semibold uppercase text-slate-400 tracking-wider">
      Recent Chats
    </div>

    {#if sessions.length === 0}
      <div class="px-3 py-6 text-center text-xs text-slate-500">
        No conversations yet in this workspace.
      </div>
    {:else}
      {#each sessions as session (session.id)}
        <div
          class="group flex items-center justify-between px-2.5 py-2 rounded-lg text-xs cursor-pointer transition-colors {activeSession?.id === session.id ? 'bg-slate-800 text-white font-medium border border-slate-700/50' : 'text-slate-400 hover:bg-slate-800/50 hover:text-slate-200'}"
          onclick={() => onSelectSession(session)}
        >
          <div class="flex items-center gap-2 truncate flex-1 mr-1">
            <MessageSquare size={14} class="shrink-0 {activeSession?.id === session.id ? 'text-sky-400' : 'text-slate-500'}" />
            <span class="truncate">{session.title}</span>
          </div>

          <!-- Hover Action buttons -->
          <div class="opacity-0 group-hover:opacity-100 flex items-center gap-1 transition-opacity">
            <button
              title="Rename"
              class="p-1 hover:text-sky-400 rounded text-slate-400"
              onclick={(e) => {
                e.stopPropagation();
                onRenameSession(session);
              }}
            >
              <Edit2 size={12} />
            </button>
            <button
              title="Delete"
              class="p-1 hover:text-rose-400 rounded text-slate-400"
              onclick={(e) => {
                e.stopPropagation();
                onDeleteSession(session);
              }}
            >
              <Trash2 size={12} />
            </button>
          </div>
        </div>
      {/each}
    {/if}
  </div>

  <!-- Bottom Workspace Indicator -->
  <div class="p-3 border-t border-slate-800 text-[11px] text-slate-400 flex items-center justify-between bg-slate-950/40">
    <span class="truncate" title={activeWorkspace?.path}>{activeWorkspace?.path || "C:\\"}</span>
    <span class="text-sky-400 font-mono shrink-0 ml-2">{activeWorkspace?.model}</span>
  </div>
</aside>
