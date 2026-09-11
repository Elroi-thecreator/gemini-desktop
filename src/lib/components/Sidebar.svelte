<script lang="ts">
  import type { Workspace, Session } from "$lib/types";
  import { themeManager } from "$lib/theme.svelte";
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
    Palette,
    Server,
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
    onOpenThemeModal,
    onOpenMcpModal,
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
    onOpenThemeModal: () => void;
    onOpenMcpModal: () => void;
    envStatus: any;
  } = $props();

  let showWorkspaceMenu = $state(false);
</script>

<aside class="w-72 h-screen flex flex-col bg-sidebar border-r border-subtle select-none">
  <!-- Top App Brand -->
  <div class="p-3.5 border-b border-subtle flex items-center justify-between">
    <div class="flex items-center gap-2.5">
      <div
        class="w-8 h-8 rounded-lg flex items-center justify-center text-white shadow-md shadow-black/20"
        style="background: var(--accent-gradient);"
      >
        <Sparkles size={18} />
      </div>
      <div>
        <h1 class="text-sm font-semibold tracking-tight text-primary-theme flex items-center gap-1.5">
          Gemini Desktop
        </h1>
        <div class="flex items-center gap-1.5 text-[11px] text-muted-theme">
          <span class="inline-block w-1.5 h-1.5 rounded-full {envStatus?.installed ? 'bg-emerald-400' : 'bg-amber-400'}"></span>
          <span>{envStatus?.installed ? 'CLI Connected' : 'Mock / Standby'}</span>
        </div>
      </div>
    </div>
  </div>

  <!-- Workspace Selector -->
  <div class="p-3 border-b border-subtle">
    <div class="text-[11px] font-semibold uppercase text-muted-theme tracking-wider mb-1.5 px-1">
      Workspace Profile
    </div>
    <div class="relative">
      <button
        onclick={() => (showWorkspaceMenu = !showWorkspaceMenu)}
        class="w-full flex items-center justify-between px-3 py-2 rounded-lg bg-surface hover:bg-surface-hover border border-theme-default text-xs text-primary-theme transition-all text-left"
      >
        <div class="flex items-center gap-2 truncate">
          <FolderKanban size={14} class="text-accent-theme shrink-0" />
          <span class="truncate font-medium">{activeWorkspace?.name || "Select Workspace"}</span>
        </div>
        <ChevronDown size={14} class="text-secondary-theme shrink-0" />
      </button>

      {#if showWorkspaceMenu}
        <div class="absolute left-0 right-0 mt-1 bg-surface-elevated border border-subtle rounded-lg shadow-xl py-1 z-30">
          {#each workspaces as ws}
            <button
              onclick={() => {
                onSelectWorkspace(ws);
                showWorkspaceMenu = false;
              }}
              class="w-full px-3 py-1.5 text-xs text-left flex items-center justify-between hover:bg-surface-hover transition-colors {activeWorkspace?.id === ws.id ? 'text-accent-theme font-medium' : 'text-secondary-theme'}"
            >
              <span class="truncate">{ws.name}</span>
              <span class="text-[10px] text-muted-theme font-mono">{ws.model}</span>
            </button>
          {/each}
          <div class="border-t border-subtle my-1"></div>
          <button
            onclick={() => {
              showWorkspaceMenu = false;
              onOpenWorkspaceModal();
            }}
            class="w-full px-3 py-1.5 text-xs text-left text-accent-theme hover:bg-surface-hover flex items-center gap-1.5"
          >
            <Settings size={12} />
            <span>Manage Workspaces...</span>
          </button>
        </div>
      {/if}
    </div>
  </div>

  <!-- Quick Action Navigation -->
  <div class="p-3 space-y-1 border-b border-subtle">
    <button
      onclick={onNewSession}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg bg-surface hover:bg-surface-hover text-primary-theme hover:border-accent-theme/50 border border-theme-default text-xs font-medium transition-all shadow-2xs group cursor-pointer"
    >
      <Plus size={15} class="text-accent-theme group-hover:scale-110 transition-transform" />
      <span>New Chat</span>
      <span class="ml-auto text-[10px] text-muted-theme font-mono">Ctrl+N</span>
    </button>

    <button
      onclick={onOpenSearch}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-surface-hover text-secondary-theme hover:text-primary-theme text-xs transition-colors"
    >
      <Search size={14} class="text-muted-theme" />
      <span>Search History</span>
      <span class="ml-auto text-[10px] text-muted-theme font-mono">Ctrl+K</span>
    </button>

    <button
      onclick={onOpenTemplates}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-surface-hover text-secondary-theme hover:text-primary-theme text-xs transition-colors"
    >
      <BookOpen size={14} class="text-muted-theme" />
      <span>Prompt Library</span>
    </button>

    <button
      onclick={onOpenThemeModal}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-surface-hover text-secondary-theme hover:text-primary-theme text-xs transition-colors"
    >
      <Palette size={14} class="text-accent-theme" />
      <span>Theme & Colors</span>
      <span class="ml-auto text-[10px] capitalize text-muted-theme font-medium">{themeManager.current.replace('-', ' ')}</span>
    </button>

    <button
      onclick={onOpenMcpModal}
      class="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-surface-hover text-secondary-theme hover:text-primary-theme text-xs transition-colors cursor-pointer"
    >
      <Server size={14} class="text-accent-theme" />
      <span>MCP Servers</span>
      <span class="ml-auto text-[10px] text-muted-theme font-mono">Ctrl+M</span>
    </button>
  </div>

  <!-- Chat History Sessions List -->
  <div class="flex-1 overflow-y-auto p-2 space-y-0.5">
    <div class="px-2 py-1.5 text-[11px] font-semibold uppercase text-muted-theme tracking-wider">
      Recent Chats
    </div>

    {#if sessions.length === 0}
      <div class="px-3 py-6 text-center text-xs text-muted-theme">
        No conversations yet in this workspace.
      </div>
    {:else}
      {#each sessions as session (session.id)}
        <div
          class="group flex items-center justify-between px-2.5 py-2 rounded-lg text-xs cursor-pointer transition-colors {activeSession?.id === session.id ? 'bg-surface-elevated text-primary-theme font-medium border border-subtle shadow-xs' : 'text-secondary-theme hover:bg-surface-hover hover:text-primary-theme'}"
          onclick={() => onSelectSession(session)}
        >
          <div class="flex items-center gap-2 truncate flex-1 mr-1">
            <MessageSquare size={14} class="shrink-0 {activeSession?.id === session.id ? 'text-accent-theme' : 'text-muted-theme'}" />
            <span class="truncate">{session.title}</span>
          </div>

          <!-- Hover Action buttons -->
          <div class="opacity-0 group-hover:opacity-100 flex items-center gap-1 transition-opacity">
            <button
              title="Rename"
              class="p-1 hover:text-accent-theme rounded text-secondary-theme"
              onclick={(e) => {
                e.stopPropagation();
                onRenameSession(session);
              }}
            >
              <Edit2 size={12} />
            </button>
            <button
              title="Delete"
              class="p-1 hover:text-rose-400 rounded text-secondary-theme"
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
  <div class="p-3 border-t border-subtle text-[11px] text-muted-theme flex items-center justify-between bg-surface/40">
    <span class="truncate" title={activeWorkspace?.path}>{activeWorkspace?.path || "C:\\"}</span>
    <span class="text-accent-theme font-mono shrink-0 ml-2">{activeWorkspace?.model}</span>
  </div>
</aside>
