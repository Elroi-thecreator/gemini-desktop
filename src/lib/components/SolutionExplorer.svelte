<script lang="ts">
  import type { Workspace, WorkspaceFileEntry } from "$lib/types";
  import {
    Folder,
    FolderOpen,
    ChevronRight,
    Search,
    X,
    RotateCw,
    ChevronsDownUp,
    ChevronsUpDown,
    PanelRightClose,
    PanelRight,
    Copy,
    ExternalLink,
    FileCode,
    FileText,
    Check,
    Layers,
    AtSign,
    Database,
    Terminal,
    Image,
    Eye,
    EyeOff,
    FolderTree,
    Paperclip,
    CheckSquare,
    Square,
  } from "lucide-svelte";
  import Tooltip from "$lib/components/Tooltip.svelte";
  import { openPath, revealItemInDir } from "@tauri-apps/plugin-opener";

  export interface TreeNode {
    name: string;
    path: string;
    isDir: boolean;
    extension?: string;
    children: TreeNode[];
  }

  let {
    workspace = null,
    workspaceFiles = [],
    isOpen = true,
    onToggle,
    onRefresh,
    onInsertMention,
    onAttachItem,
    onAttachMultiple,
  }: {
    workspace: Workspace | null;
    workspaceFiles: WorkspaceFileEntry[];
    isOpen: boolean;
    onToggle: () => void;
    onRefresh?: () => void;
    onInsertMention?: (relativePath: string) => void;
    onAttachItem?: (relativePath: string, isDir: boolean, name?: string) => void;
    onAttachMultiple?: (items: { path: string; isDir: boolean; name?: string }[]) => void;
  } = $props();

  // State
  let searchQuery = $state("");
  let expandedDirs = $state<Set<string>>(new Set());
  let selectedNode = $state<TreeNode | null>(null);
  let copiedPath = $state<string | null>(null);
  let attachedPath = $state<string | null>(null);
  let isRefreshing = $state(false);
  let searchInputElem: HTMLInputElement | null = $state(null);

  // Hidden files toggle state (hidden by default)
  let showHiddenFiles = $state(false);

  // Multi-select / Choose mode
  let isSelectMode = $state(false);
  let checkedItems = $state<Map<string, { path: string; isDir: boolean; name: string }>>(new Map());

  // Context Menu State
  let contextMenu = $state<{ x: number; y: number; node: TreeNode } | null>(null);

  // Resizable Panel Width
  let explorerWidth = $state(310);
  let isDragging = $state(false);

  // Build tree from flat workspaceFiles (hiding dotfiles unless showHiddenFiles is true)
  let treeData = $derived.by(() => {
    return buildTree(workspaceFiles, searchQuery, showHiddenFiles);
  });

  // Count visible and hidden files/folders
  let visibleFilesCount = $derived.by(() => {
    return workspaceFiles.filter((f) => {
      if (f.is_dir) return false;
      if (!showHiddenFiles && f.relative_path.split("/").some((p) => p.startsWith("."))) return false;
      return true;
    }).length;
  });

  let visibleDirsCount = $derived.by(() => {
    return workspaceFiles.filter((f) => {
      if (!f.is_dir) return false;
      if (!showHiddenFiles && f.relative_path.split("/").some((p) => p.startsWith("."))) return false;
      return true;
    }).length;
  });

  let hiddenItemsCount = $derived.by(() => {
    return workspaceFiles.filter((f) => {
      return f.relative_path.split("/").some((p) => p.startsWith("."));
    }).length;
  });

  let currentWorkspaceId = $state<string | null>(null);
  let preSearchExpandedDirs = $state<Set<string>>(new Set());
  let wasSearching = $state(false);

  // Keep all folders in a collapsed state by default for optimal performance.
  // Reset expansion and selection when switching workspaces.
  $effect(() => {
    if (workspace?.id !== currentWorkspaceId) {
      currentWorkspaceId = workspace?.id || null;
      expandedDirs = new Set();
      checkedItems = new Map();
      selectedNode = null;
    }
  });

  // When search query is active, auto-expand matching branches, and restore collapsed state when cleared
  $effect(() => {
    const q = searchQuery.trim().toLowerCase();
    if (q) {
      if (!wasSearching) {
        preSearchExpandedDirs = new Set(expandedDirs);
        wasSearching = true;
      }
      const allDirs = new Set(expandedDirs);
      function expandMatching(nodes: TreeNode[]) {
        for (const n of nodes) {
          if (n.isDir) {
            allDirs.add(n.path);
            expandMatching(n.children);
          }
        }
      }
      expandMatching(treeData);
      expandedDirs = allDirs;
    } else if (wasSearching) {
      expandedDirs = preSearchExpandedDirs;
      wasSearching = false;
    }
  });

  function buildTree(entries: WorkspaceFileEntry[], query: string, showHidden: boolean): TreeNode[] {
    const nodeMap = new Map<string, TreeNode>();
    const rootNodes: TreeNode[] = [];
    const trimmedQuery = query.trim().toLowerCase();
    const querySearchesHidden = trimmedQuery.startsWith(".");

    for (const entry of entries) {
      const parts = entry.relative_path.split("/");

      // Filter out hidden files and folders (starting with '.') unless showHidden is true
      // or user explicitly types a query starting with '.'
      if (!showHidden && !querySearchesHidden) {
        if (parts.some((p) => p.startsWith("."))) {
          continue;
        }
      }

      let currentPath = "";

      for (let i = 0; i < parts.length; i++) {
        const part = parts[i];
        const isLast = i === parts.length - 1;
        const partPath = currentPath ? `${currentPath}/${part}` : part;

        if (!nodeMap.has(partPath)) {
          const isDir = isLast ? entry.is_dir : true;
          const ext = isLast ? entry.extension : undefined;
          const node: TreeNode = {
            name: part,
            path: partPath,
            isDir,
            extension: ext,
            children: [],
          };
          nodeMap.set(partPath, node);

          if (i === 0) {
            rootNodes.push(node);
          } else {
            const parent = nodeMap.get(currentPath);
            if (parent && !parent.children.some((c) => c.path === partPath)) {
              parent.children.push(node);
            }
          }
        }
        currentPath = partPath;
      }
    }

    // Sort: directories first (alphabetical), then files (alphabetical)
    function sortNodes(nodes: TreeNode[]) {
      nodes.sort((a, b) => {
        if (a.isDir !== b.isDir) {
          return a.isDir ? -1 : 1;
        }
        return a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
      });
      for (const node of nodes) {
        if (node.children.length > 0) {
          sortNodes(node.children);
        }
      }
    }
    sortNodes(rootNodes);

    // Apply search filter if query present
    if (trimmedQuery) {
      function filterNode(node: TreeNode): TreeNode | null {
        const selfMatch =
          node.name.toLowerCase().includes(trimmedQuery) ||
          node.path.toLowerCase().includes(trimmedQuery);
        const filteredChildren: TreeNode[] = [];

        for (const child of node.children) {
          const res = filterNode(child);
          if (res) filteredChildren.push(res);
        }

        if (selfMatch || filteredChildren.length > 0) {
          return {
            ...node,
            children: filteredChildren,
          };
        }
        return null;
      }

      const filteredRoots: TreeNode[] = [];
      for (const root of rootNodes) {
        const res = filterNode(root);
        if (res) filteredRoots.push(res);
      }
      return filteredRoots;
    }

    return rootNodes;
  }

  function toggleDir(path: string, e?: MouseEvent) {
    if (e) e.stopPropagation();
    const next = new Set(expandedDirs);
    if (next.has(path)) {
      next.delete(path);
    } else {
      next.add(path);
    }
    expandedDirs = next;
  }

  function collapseAll() {
    expandedDirs = new Set();
  }

  function expandAll() {
    const all = new Set<string>();
    function collectDirs(nodes: TreeNode[]) {
      for (const n of nodes) {
        if (n.isDir) {
          all.add(n.path);
          collectDirs(n.children);
        }
      }
    }
    collectDirs(treeData);
    expandedDirs = all;
  }

  async function handleRefreshClick() {
    if (isRefreshing) return;
    isRefreshing = true;
    try {
      if (onRefresh) await onRefresh();
    } finally {
      setTimeout(() => {
        isRefreshing = false;
      }, 500);
    }
  }

  function handleAttachNode(node: TreeNode, e?: MouseEvent) {
    if (e) e.stopPropagation();
    if (onAttachItem) {
      onAttachItem(node.path, node.isDir, node.name);
      attachedPath = node.path;
      setTimeout(() => {
        if (attachedPath === node.path) attachedPath = null;
      }, 1500);
    }
    contextMenu = null;
  }

  function handleAttachChecked() {
    if (checkedItems.size === 0) return;
    const items = Array.from(checkedItems.values());
    if (onAttachMultiple) {
      onAttachMultiple(items);
    } else if (onAttachItem) {
      for (const it of items) {
        onAttachItem(it.path, it.isDir, it.name);
      }
    }
    checkedItems = new Map();
    isSelectMode = false;
  }

  function toggleCheckItem(node: TreeNode, e?: Event) {
    if (e) e.stopPropagation();
    const next = new Map(checkedItems);
    if (next.has(node.path)) {
      next.delete(node.path);
    } else {
      next.set(node.path, { path: node.path, isDir: node.isDir, name: node.name });
    }
    checkedItems = next;
  }

  function clearChecked() {
    checkedItems = new Map();
  }

  function handleInsertMention(node: TreeNode, e?: MouseEvent) {
    if (e) e.stopPropagation();
    if (onInsertMention) {
      const mentionPath = node.isDir
        ? node.path.endsWith("/") ? node.path : `${node.path}/`
        : node.path;
      onInsertMention(mentionPath);
    }
    contextMenu = null;
  }

  async function handleCopyPath(nodePath: string, e?: MouseEvent) {
    if (e) e.stopPropagation();
    try {
      await navigator.clipboard.writeText(nodePath);
      copiedPath = nodePath;
      setTimeout(() => {
        if (copiedPath === nodePath) copiedPath = null;
      }, 1500);
    } catch (err) {
      console.error("Failed to copy path:", err);
    }
    contextMenu = null;
  }

  function resolveFullPath(relPath: string): string {
    if (!workspace?.path) return relPath;
    const base = workspace.path.replace(/[/\\]+$/, "");
    return `${base}/${relPath}`.replace(/\//g, "\\");
  }

  async function handleOpenFile(nodePath: string, e?: MouseEvent) {
    if (e) e.stopPropagation();
    const full = resolveFullPath(nodePath);
    try {
      await openPath(full);
    } catch (err) {
      console.warn("Could not open file via system default:", err);
    }
    contextMenu = null;
  }

  async function handleRevealInExplorer(nodePath: string, e?: MouseEvent) {
    if (e) e.stopPropagation();
    const full = resolveFullPath(nodePath);
    try {
      await revealItemInDir(full);
    } catch (err) {
      console.warn("Could not reveal file in Windows explorer:", err);
    }
    contextMenu = null;
  }

  function handleContextMenu(node: TreeNode, e: MouseEvent) {
    e.preventDefault();
    e.stopPropagation();
    selectedNode = node;
    const clickX = e.clientX;
    const clickY = e.clientY;
    // Keep context menu within viewport bounds
    const x = Math.min(clickX, window.innerWidth - 200);
    const y = Math.min(clickY, window.innerHeight - 240);
    contextMenu = { x, y, node };
  }

  // Visual Studio 2022 File Icon & Color Mapping
  function getFileIconMeta(node: TreeNode) {
    if (node.isDir) {
      return {
        type: "folder",
        color: "#f59e0b", // Classic VS yellow/amber
        badge: "",
      };
    }

    const ext = (node.extension || "").toLowerCase();
    const lowerName = node.name.toLowerCase();

    // Specific file names
    if (lowerName === "cargo.toml" || lowerName === "cargo.lock") {
      return { type: "code", color: "#f97316", badge: "CRG" };
    }
    if (lowerName === "package.json") {
      return { type: "code", color: "#eab308", badge: "{}" };
    }
    if (lowerName === "gemini.md" || lowerName === "agents.md") {
      return { type: "text", color: "#38bdf8", badge: "AI" };
    }
    if (lowerName.startsWith(".git")) {
      return { type: "text", color: "#f97316", badge: "GIT" };
    }

    // Extensions
    switch (ext) {
      case "cs":
        return { type: "code", color: "#a855f7", badge: "C#" };
      case "rs":
        return { type: "code", color: "#f97316", badge: "RS" };
      case "ts":
      case "tsx":
        return { type: "code", color: "#3b82f6", badge: "TS" };
      case "js":
      case "jsx":
      case "mjs":
        return { type: "code", color: "#eab308", badge: "JS" };
      case "svelte":
        return { type: "code", color: "#ff3e00", badge: "SV" };
      case "html":
      case "htm":
        return { type: "code", color: "#f97316", badge: "<>" };
      case "css":
      case "scss":
      case "sass":
      case "less":
        return { type: "code", color: "#06b6d4", badge: "#" };
      case "json":
        return { type: "code", color: "#fbbf24", badge: "{}" };
      case "md":
      case "markdown":
        return { type: "text", color: "#38bdf8", badge: "MD" };
      case "yaml":
      case "yml":
        return { type: "text", color: "#c084fc", badge: "YML" };
      case "toml":
      case "ini":
      case "conf":
      case "cfg":
        return { type: "text", color: "#fb923c", badge: "CFG" };
      case "sql":
      case "db":
      case "sqlite":
        return { type: "database", color: "#14b8a6", badge: "SQL" };
      case "sh":
      case "bash":
      case "bat":
      case "cmd":
      case "ps1":
        return { type: "terminal", color: "#22c55e", badge: ">_" };
      case "png":
      case "jpg":
      case "jpeg":
      case "gif":
      case "svg":
      case "ico":
      case "webp":
        return { type: "image", color: "#c084fc", badge: "IMG" };
      default:
        return { type: "text", color: "#94a3b8", badge: "" };
    }
  }

  // Handle panel resizing via dragging left border
  function handleMouseDown(e: MouseEvent) {
    e.preventDefault();
    isDragging = true;
    const startX = e.clientX;
    const startWidth = explorerWidth;

    function handleMouseMove(moveEvent: MouseEvent) {
      const deltaX = startX - moveEvent.clientX;
      explorerWidth = Math.max(240, Math.min(600, startWidth + deltaX));
    }

    function handleMouseUp() {
      isDragging = false;
      window.removeEventListener("mousemove", handleMouseMove);
      window.removeEventListener("mouseup", handleMouseUp);
    }

    window.addEventListener("mousemove", handleMouseMove);
    window.addEventListener("mouseup", handleMouseUp);
  }

  export function focusSearch() {
    if (searchInputElem) {
      searchInputElem.focus();
      searchInputElem.select();
    }
  }
</script>

{#if !isOpen}
  <!-- Workspace Explorer Auto-Hide Collapsed Dock Tab on the Right Margin -->
  <aside
    class="w-7 h-full bg-sidebar border-l border-subtle flex flex-col items-center py-3 select-none z-10 transition-colors hover:bg-surface"
    aria-label="Workspace Explorer collapsed dock"
  >
    <Tooltip text="Expand Workspace Explorer" shortcut="Ctrl+Alt+L" position="left">
      <button
        type="button"
        onclick={onToggle}
        class="w-full flex flex-col items-center gap-3 text-secondary-theme hover:text-accent-theme cursor-pointer group focus:outline-none"
        aria-label="Expand Workspace Explorer panel"
      >
        <div class="p-1 rounded group-hover:bg-surface-elevated text-accent-theme">
          <Layers size={14} class="text-[#a855f7]" />
        </div>
        <span
          class="text-[11px] font-medium tracking-wide uppercase text-muted-theme group-hover:text-primary-theme"
          style="writing-mode: vertical-rl; transform: rotate(180deg);"
        >
          Workspace Explorer
        </span>
      </button>
    </Tooltip>
  </aside>
{:else}
  <!-- Workspace Explorer Full Right Panel -->
  <aside
    class="relative h-full bg-sidebar border-l border-theme-default flex flex-col shrink-0 select-none z-10 overflow-hidden shadow-lg transition-all"
    style="width: {explorerWidth}px;"
    aria-label="Workspace Explorer"
  >
    <!-- Left Resizer Drag Handle -->
    <button
      type="button"
      onmousedown={handleMouseDown}
      aria-label="Resize Workspace Explorer"
      class="absolute left-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-accent-theme transition-colors z-20 {isDragging ? 'bg-accent-theme' : 'bg-transparent'}"
    ></button>

    <!-- Top Header: Workspace Explorer Title & Toolbar -->
    <header class="h-[34px] min-h-[34px] px-2.5 bg-surface border-b border-subtle flex items-center justify-between gap-1">
      <div class="flex items-center gap-1.5 truncate">
        <!-- Explorer Cube Icon -->
        <div class="w-4 h-4 flex items-center justify-center text-[#a855f7] shrink-0">
          <Layers size={14} />
        </div>
        <span class="text-xs font-semibold text-primary-theme tracking-tight truncate">
          Workspace Explorer
        </span>
      </div>

      <!-- Action Icons Toolbar -->
      <div class="flex items-center gap-0.5 shrink-0 text-muted-theme">
        <!-- Choose / Multi-Select Mode -->
        <Tooltip text={isSelectMode ? "Exit Selection Mode" : "Choose / Multi-Select Items to Attach"} position="bottom">
          <button
            type="button"
            onclick={() => {
              isSelectMode = !isSelectMode;
              if (!isSelectMode) checkedItems = new Map();
            }}
            class="p-1 rounded hover:bg-surface-hover transition-colors cursor-pointer {isSelectMode ? 'bg-accent-subtle text-accent-theme' : 'hover:text-primary-theme'}"
            aria-label="Toggle multi-select mode"
          >
            <CheckSquare size={13} />
          </button>
        </Tooltip>

        <!-- Toggle Hidden Files & Folders -->
        <Tooltip
          text={showHiddenFiles
            ? "Hide Hidden Files & Folders (.*)"
            : `Show Hidden Files & Folders${hiddenItemsCount > 0 ? ` (${hiddenItemsCount} hidden)` : ""}`}
          position="bottom"
        >
          <button
            type="button"
            onclick={() => (showHiddenFiles = !showHiddenFiles)}
            class="p-1 rounded hover:bg-surface-hover transition-colors cursor-pointer {showHiddenFiles ? 'bg-accent-subtle text-accent-theme' : 'hover:text-primary-theme'}"
            aria-label={showHiddenFiles ? "Hide hidden files" : "Show hidden files"}
          >
            {#if showHiddenFiles}
              <Eye size={13} />
            {:else}
              <EyeOff size={13} />
            {/if}
          </button>
        </Tooltip>

        <!-- Refresh -->
        <Tooltip text="Refresh Files" shortcut="F5" position="bottom">
          <button
            type="button"
            onclick={handleRefreshClick}
            class="p-1 rounded hover:bg-surface-hover hover:text-primary-theme transition-colors cursor-pointer"
            aria-label="Refresh workspace files"
          >
            <RotateCw size={13} class={isRefreshing ? "animate-spin text-accent-theme" : ""} />
          </button>
        </Tooltip>

        <!-- Collapse All -->
        <Tooltip text="Collapse All Folders" position="bottom">
          <button
            type="button"
            onclick={collapseAll}
            class="p-1 rounded hover:bg-surface-hover hover:text-primary-theme transition-colors cursor-pointer"
            aria-label="Collapse all folders"
          >
            <ChevronsDownUp size={13} />
          </button>
        </Tooltip>

        <!-- Expand All -->
        <Tooltip text="Expand All Folders" position="bottom">
          <button
            type="button"
            onclick={expandAll}
            class="p-1 rounded hover:bg-surface-hover hover:text-primary-theme transition-colors cursor-pointer"
            aria-label="Expand all folders"
          >
            <ChevronsUpDown size={13} />
          </button>
        </Tooltip>

        <!-- Collapse to Right (Close/Dock) -->
        <Tooltip text="Collapse to Right" shortcut="Ctrl+Alt+L" position="bottom">
          <button
            type="button"
            onclick={onToggle}
            class="p-1 rounded hover:bg-surface-hover hover:text-primary-theme transition-colors cursor-pointer"
            aria-label="Collapse Workspace Explorer to right"
          >
            <PanelRightClose size={13} />
          </button>
        </Tooltip>
      </div>
    </header>

    <!-- Search / Filter Bar -->
    <div class="px-2 py-1.5 bg-sidebar border-b border-subtle">
      <div class="relative flex items-center">
        <Search size={12} class="absolute left-2 text-muted-theme pointer-events-none" />
        <input
          bind:this={searchInputElem}
          type="text"
          bind:value={searchQuery}
          placeholder="Search Workspace Explorer (Ctrl+;)"
          class="w-full pl-7 pr-6 py-1 text-xs bg-surface text-primary-theme placeholder:text-muted-theme rounded border border-theme-default focus:border-accent-theme focus:outline-none transition-colors"
        />
        {#if searchQuery}
          <button
            type="button"
            onclick={() => {
              searchQuery = "";
              searchInputElem?.focus();
            }}
            class="absolute right-1.5 p-0.5 text-muted-theme hover:text-primary-theme cursor-pointer"
            aria-label="Clear search"
          >
            <X size={12} />
          </button>
        {/if}
      </div>
    </div>

    <!-- Multi-Select Action Banner (When Items are Checked) -->
    {#if isSelectMode || checkedItems.size > 0}
      <div class="px-2.5 py-1.5 bg-surface-elevated border-b border-subtle flex items-center justify-between gap-2 text-xs">
        <span class="text-accent-theme font-medium flex items-center gap-1.5 truncate">
          <CheckSquare size={13} />
          <span>{checkedItems.size} chosen</span>
        </span>
        <div class="flex items-center gap-1.5 shrink-0">
          {#if checkedItems.size > 0}
            <button
              type="button"
              onclick={clearChecked}
              class="text-[11px] text-muted-theme hover:text-primary-theme px-1.5 py-0.5 rounded cursor-pointer"
            >
              Clear
            </button>
            <button
              type="button"
              onclick={handleAttachChecked}
              class="flex items-center gap-1 px-2 py-0.5 rounded bg-accent-theme hover:bg-accent-hover text-white font-medium text-[11px] transition-colors cursor-pointer shadow-xs"
            >
              <Paperclip size={11} />
              <span>Attach to Chat</span>
            </button>
          {:else}
            <span class="text-[11px] text-muted-theme">Check files or folders below</span>
          {/if}
        </div>
      </div>
    {/if}

    <!-- Tree Structure Container -->
    <div class="flex-1 overflow-y-auto overflow-x-hidden py-1 text-xs font-sans">
      <!-- Workspace Root Node -->
      <div
        class="flex items-center justify-between px-2 py-1 text-xs font-medium text-secondary-theme bg-surface-elevated/40 border-b border-subtle/50 select-none group"
      >
        <div class="flex items-center gap-1.5 truncate">
          <div class="w-3.5 h-3.5 flex items-center justify-center text-[#a855f7] shrink-0">
            <Layers size={13} />
          </div>
          <span class="truncate" title={workspace?.path || "No workspace root"}>
            Workspace '{workspace?.name || "Workspace"}'
          </span>
        </div>
        <div class="flex items-center gap-1 shrink-0">
          <span class="text-[10px] text-muted-theme font-mono">
            {visibleFilesCount} files, {visibleDirsCount} folders{#if !showHiddenFiles && hiddenItemsCount > 0}
              <span class="opacity-75"> ({hiddenItemsCount} hidden)</span>
            {/if}
          </span>
        </div>
      </div>

      {#if treeData.length === 0}
        <div class="p-4 text-center text-muted-theme text-xs flex flex-col items-center gap-2">
          <FolderTree size={24} class="opacity-40 text-muted-theme" />
          {#if searchQuery}
            <span>No files or folders match "{searchQuery}"</span>
            <button
              type="button"
              onclick={() => (searchQuery = "")}
              class="text-accent-theme hover:underline cursor-pointer"
            >
              Clear search filter
            </button>
          {:else if hiddenItemsCount > 0 && !showHiddenFiles}
            <span>Only hidden files exist ({hiddenItemsCount} hidden)</span>
            <button
              type="button"
              onclick={() => (showHiddenFiles = true)}
              class="text-accent-theme hover:underline cursor-pointer"
            >
              Show hidden files
            </button>
          {:else}
            <span>No files found in this workspace</span>
            {#if onRefresh}
              <button
                type="button"
                onclick={handleRefreshClick}
                class="text-accent-theme hover:underline cursor-pointer"
              >
                Scan workspace files
              </button>
            {/if}
          {/if}
        </div>
      {:else}
        <!-- Tree Nodes with Recursive Snippet -->
        <div class="py-0.5">
          {#each treeData as node (node.path)}
            {@render treeRow(node, 0)}
          {/each}
        </div>
      {/if}
    </div>

    <!-- Bottom Status Strip -->
    <footer class="h-7 min-h-7 px-2.5 bg-surface border-t border-subtle flex items-center justify-between text-[11px] text-muted-theme">
      <div class="truncate flex items-center gap-1.5 max-w-[170px]">
        {#if selectedNode}
          <span class="text-primary-theme font-mono truncate" title={selectedNode.path}>
            {selectedNode.path}
          </span>
        {:else}
          <span>Ready</span>
        {/if}
      </div>
      <div class="flex items-center gap-1 shrink-0">
        {#if selectedNode}
          <Tooltip text={selectedNode.isDir ? "Attach selected folder to chat" : "Attach selected file to chat"} position="top">
            <button
              type="button"
              onclick={() => {
                if (selectedNode) handleAttachNode(selectedNode);
              }}
              class="flex items-center gap-1 px-1.5 py-0.5 rounded bg-surface-elevated hover:bg-surface-hover text-accent-theme border border-subtle font-medium text-[10px] cursor-pointer transition-colors"
            >
              <Paperclip size={10} />
              <span>Attach</span>
            </button>
          </Tooltip>
        {/if}
        <span class="font-mono text-[10px] text-muted-theme">
          {visibleFilesCount} files
        </span>
      </div>
    </footer>
  </aside>
{/if}

<!-- Context Menu Popup -->
{#if contextMenu}
  <!-- Backdrop to close context menu -->
  <div
    class="fixed inset-0 z-50 bg-transparent"
    onclick={() => (contextMenu = null)}
    oncontextmenu={(e) => {
      e.preventDefault();
      contextMenu = null;
    }}
    role="presentation"
  ></div>

  <!-- Context Menu Card -->
  <div
    class="fixed z-50 w-48 bg-surface-elevated border border-subtle rounded-lg shadow-2xl py-1 text-xs text-primary-theme"
    style="left: {contextMenu.x}px; top: {contextMenu.y}px;"
  >
    <div class="px-2.5 py-1 text-[10px] text-muted-theme border-b border-subtle/60 font-mono truncate">
      {contextMenu.node.name}
    </div>

    <!-- Attach to Chat -->
    <button
      type="button"
      onclick={() => {
        if (contextMenu) handleAttachNode(contextMenu.node);
      }}
      class="w-full px-2.5 py-1.5 text-left hover:bg-surface-hover flex items-center gap-2 text-accent-theme font-medium cursor-pointer"
    >
      <Paperclip size={13} />
      <span>Attach to Chat</span>
    </button>

    <!-- Mention in Chat -->
    <button
      type="button"
      onclick={() => {
        if (contextMenu) handleInsertMention(contextMenu.node);
      }}
      class="w-full px-2.5 py-1.5 text-left hover:bg-surface-hover flex items-center gap-2 cursor-pointer"
    >
      <AtSign size={13} class="text-secondary-theme" />
      <span>Mention in Prompt (@)</span>
    </button>

    <div class="my-1 border-t border-subtle/60"></div>

    {#if !contextMenu.node.isDir}
      <!-- Open File -->
      <button
        type="button"
        onclick={() => {
          if (contextMenu) handleOpenFile(contextMenu.node.path);
        }}
        class="w-full px-2.5 py-1.5 text-left hover:bg-surface-hover flex items-center gap-2 cursor-pointer"
      >
        <ExternalLink size={13} class="text-secondary-theme" />
        <span>Open with Default App</span>
      </button>
    {/if}

    <!-- Reveal in Explorer -->
    <button
      type="button"
      onclick={() => {
        if (contextMenu) handleRevealInExplorer(contextMenu.node.path);
      }}
      class="w-full px-2.5 py-1.5 text-left hover:bg-surface-hover flex items-center gap-2 cursor-pointer"
    >
      <Eye size={13} class="text-secondary-theme" />
      <span>Reveal in Explorer</span>
    </button>

    <div class="my-1 border-t border-subtle/60"></div>

    <!-- Copy Relative Path -->
    <button
      type="button"
      onclick={() => {
        if (contextMenu) handleCopyPath(contextMenu.node.path);
      }}
      class="w-full px-2.5 py-1.5 text-left hover:bg-surface-hover flex items-center gap-2 cursor-pointer"
    >
      <Copy size={13} class="text-secondary-theme" />
      <span>Copy Relative Path</span>
    </button>
  </div>
{/if}

<!-- Svelte 5 Recursive Tree Node Snippet -->
{#snippet treeRow(node: TreeNode, depth: number)}
  {@const isExpanded = node.isDir && expandedDirs.has(node.path)}
  {@const isSelected = selectedNode?.path === node.path}
  {@const isChecked = checkedItems.has(node.path)}
  {@const isAttached = attachedPath === node.path}
  {@const meta = getFileIconMeta(node)}
  {@const isHiddenItem = node.name.startsWith(".")}

  <!-- Tree Row Item (VS 2022 Height: 24px) -->
  <div
    role="treeitem"
    aria-selected={isSelected}
    aria-expanded={node.isDir ? isExpanded : undefined}
    tabindex="0"
    onclick={() => {
      selectedNode = node;
      if (isSelectMode) {
        toggleCheckItem(node);
      } else if (node.isDir) {
        toggleDir(node.path);
      }
    }}
    ondblclick={(e) => {
      if (!node.isDir) {
        handleOpenFile(node.path, e);
      }
    }}
    oncontextmenu={(e) => handleContextMenu(node, e)}
    onkeydown={(e) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        selectedNode = node;
        if (isSelectMode) {
          toggleCheckItem(node);
        } else if (node.isDir) {
          toggleDir(node.path);
        }
      }
    }}
    class="group relative flex items-center h-6 pr-2 cursor-pointer transition-colors select-none {isSelected ? 'bg-accent-subtle text-primary-theme font-medium border-l-2 border-accent-theme' : 'text-secondary-theme hover:bg-surface-hover/80 hover:text-primary-theme'} {isHiddenItem ? 'opacity-70' : ''}"
    style="padding-left: {depth * 14 + 6}px;"
  >
    <!-- Visual Studio Tree Indentation Guide Line -->
    {#if depth > 0}
      <div
        class="absolute top-0 bottom-0 border-l border-subtle/40 pointer-events-none"
        style="left: {(depth - 1) * 14 + 11}px;"
      ></div>
    {/if}

    <!-- Optional Selection Checkbox in Select Mode -->
    {#if isSelectMode}
      <button
        type="button"
        onclick={(e) => toggleCheckItem(node, e)}
        class="w-3.5 h-3.5 mr-1 flex items-center justify-center text-muted-theme hover:text-accent-theme cursor-pointer"
        aria-label="Check item to attach"
      >
        {#if isChecked}
          <CheckSquare size={13} class="text-accent-theme" />
        {:else}
          <Square size={13} class="opacity-60" />
        {/if}
      </button>
    {/if}

    <!-- Caret Arrow for Folders / Spacer for Files -->
    {#if node.isDir}
      <button
        type="button"
        onclick={(e) => toggleDir(node.path, e)}
        class="w-3.5 h-3.5 flex items-center justify-center text-muted-theme hover:text-primary-theme shrink-0 cursor-pointer"
        aria-label={isExpanded ? "Collapse folder" : "Expand folder"}
      >
        <ChevronRight
          size={12}
          class="transition-transform duration-150 {isExpanded ? 'rotate-90 text-primary-theme' : ''}"
        />
      </button>
    {:else}
      <span class="w-3.5 h-3.5 shrink-0"></span>
    {/if}

    <!-- File / Folder Icon -->
    <div class="w-4 h-4 mr-1.5 flex items-center justify-center shrink-0">
      {#if node.isDir}
        {#if isExpanded}
          <FolderOpen size={14} class="text-[#f59e0b]" />
        {:else}
          <Folder size={14} class="text-[#f59e0b]" />
        {/if}
      {:else}
        {#if meta.type === "code"}
          <FileCode size={14} style="color: {meta.color};" />
        {:else if meta.type === "database"}
          <Database size={14} style="color: {meta.color};" />
        {:else if meta.type === "terminal"}
          <Terminal size={14} style="color: {meta.color};" />
        {:else if meta.type === "image"}
          <Image size={14} style="color: {meta.color};" />
        {:else}
          <FileText size={14} style="color: {meta.color};" />
        {/if}
      {/if}
    </div>

    <!-- Node Name -->
    <span class="truncate text-xs leading-none mr-2 font-normal {isHiddenItem ? 'italic' : ''}" title={node.path}>
      {node.name}
    </span>

    <!-- Folder Child Count Badge -->
    {#if node.isDir && node.children.length > 0}
      <span class="text-[10px] text-muted-theme/80 font-mono group-hover:opacity-0 transition-opacity ml-auto shrink-0">
        {node.children.length}
      </span>
    {/if}

    <!-- Action Buttons on Row Hover -->
    <div class="hidden group-hover:flex items-center gap-0.5 ml-auto shrink-0 bg-surface/90 rounded px-0.5 border border-subtle/50">
      <!-- Direct "Attach to Chat" Button (Available on both files and folders!) -->
      <Tooltip text={isAttached ? "Attached to Chat!" : node.isDir ? "Attach Folder to Chat" : "Attach File to Chat"} position="left">
        <button
          type="button"
          onclick={(e) => handleAttachNode(node, e)}
          class="p-0.5 rounded hover:bg-surface-elevated transition-colors cursor-pointer {isAttached ? 'text-emerald-400 font-bold' : 'text-accent-theme hover:text-accent-hover'}"
          aria-label="Attach to Chat"
        >
          {#if isAttached}
            <Check size={11} />
          {:else}
            <Paperclip size={11} />
          {/if}
        </button>
      </Tooltip>

      <!-- Insert @mention into Chat Prompt -->
      <Tooltip text={node.isDir ? "Mention Folder (@dir/)" : "Mention File (@file)"} position="left">
        <button
          type="button"
          onclick={(e) => handleInsertMention(node, e)}
          class="p-0.5 text-muted-theme hover:text-accent-theme rounded hover:bg-surface-elevated transition-colors cursor-pointer"
          aria-label="Insert mention"
        >
          <AtSign size={11} />
        </button>
      </Tooltip>

      {#if !node.isDir}
        <!-- Open with Default App -->
        <Tooltip text="Open File" position="left">
          <button
            type="button"
            onclick={(e) => handleOpenFile(node.path, e)}
            class="p-0.5 text-muted-theme hover:text-primary-theme rounded hover:bg-surface-elevated transition-colors cursor-pointer"
            aria-label="Open file"
          >
            <ExternalLink size={11} />
          </button>
        </Tooltip>
      {/if}

      <!-- Reveal in Explorer -->
      <Tooltip text="Reveal in Windows Explorer" position="left">
        <button
          type="button"
          onclick={(e) => handleRevealInExplorer(node.path, e)}
          class="p-0.5 text-muted-theme hover:text-primary-theme rounded hover:bg-surface-elevated transition-colors cursor-pointer"
          aria-label="Reveal in File Explorer"
        >
          <Eye size={11} />
        </button>
      </Tooltip>

      <!-- Copy Relative Path -->
      <Tooltip text={copiedPath === node.path ? "Copied!" : "Copy Relative Path"} position="left">
        <button
          type="button"
          onclick={(e) => handleCopyPath(node.path, e)}
          class="p-0.5 text-muted-theme hover:text-primary-theme rounded hover:bg-surface-elevated transition-colors cursor-pointer"
          aria-label="Copy path"
        >
          {#if copiedPath === node.path}
            <Check size={11} class="text-emerald-400" />
          {:else}
            <Copy size={11} />
          {/if}
        </button>
      </Tooltip>
    </div>
  </div>

  <!-- Recursive Render Children when Folder is Expanded -->
  {#if node.isDir && isExpanded}
    <div>
      {#each node.children as child (child.path)}
        {@render treeRow(child, depth + 1)}
      {/each}
    </div>
  {/if}
{/snippet}
