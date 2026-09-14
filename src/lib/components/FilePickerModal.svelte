<script lang="ts">
  import type { WorkspaceFileEntry } from "$lib/types";
  import { Search, X, File, Folder, FileCode, CornerDownLeft } from "lucide-svelte";

  let {
    isOpen = false,
    mode = "file",
    workspaceFiles = [],
    onClose,
    onSelect,
    onSelectCustomPath,
  }: {
    isOpen: boolean;
    mode: "file" | "directory" | "all";
    workspaceFiles: WorkspaceFileEntry[];
    onClose: () => void;
    onSelect: (entry: WorkspaceFileEntry) => void;
    onSelectCustomPath: (path: string) => void;
  } = $props();

  let search = $state("");
  let selectedIndex = $state(0);

  let filteredEntries = $derived.by(() => {
    const q = search.toLowerCase().trim();
    return workspaceFiles
      .filter((entry) => {
        if (mode === "file" && entry.is_dir) return false;
        if (mode === "directory" && !entry.is_dir) return false;
        if (!q) return true;
        return (
          entry.name.toLowerCase().includes(q) ||
          entry.relative_path.toLowerCase().includes(q)
        );
      })
      .slice(0, 50);
  });

  $effect(() => {
    if (search !== undefined) {
      selectedIndex = 0;
    }
  });

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Escape") {
      onClose();
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      if (filteredEntries.length > 0) {
        selectedIndex = (selectedIndex + 1) % filteredEntries.length;
      }
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      if (filteredEntries.length > 0) {
        selectedIndex = (selectedIndex - 1 + filteredEntries.length) % filteredEntries.length;
      }
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (filteredEntries.length > 0 && selectedIndex < filteredEntries.length) {
        chooseEntry(filteredEntries[selectedIndex]);
      } else if (search.trim()) {
        onSelectCustomPath(search.trim());
        onClose();
      }
    }
  }

  function chooseEntry(entry: WorkspaceFileEntry) {
    onSelect(entry);
    onClose();
  }

  function handleCustomSubmit() {
    if (search.trim()) {
      onSelectCustomPath(search.trim());
      onClose();
    }
  }

  let searchInputEl: HTMLInputElement | null = $state(null);

  $effect(() => {
    if (isOpen) {
      setTimeout(() => searchInputEl?.focus(), 50);
    }
  });
</script>

{#if isOpen}
  <div
    class="fixed inset-0 bg-black/65 backdrop-blur-xs z-50 flex items-start justify-center pt-20 p-4"
    onclick={onClose}
    onkeydown={handleKeyDown}
    role="presentation"
  >
    <div
      class="w-full max-w-xl bg-surface border border-theme-default rounded-2xl shadow-2xl overflow-hidden flex flex-col max-h-[75vh]"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => e.stopPropagation()}
      role="dialog"
      aria-modal="true"
      tabindex="-1"
    >
      <div class="px-4 py-3 border-b border-subtle flex items-center justify-between bg-surface-elevated/40">
        <div class="flex items-center gap-2">
          {#if mode === "directory"}
            <Folder size={16} class="text-sky-400" />
            <span class="text-xs font-semibold text-primary-theme">Attach Workspace Directory</span>
          {:else}
            <FileCode size={16} class="text-accent-theme" />
            <span class="text-xs font-semibold text-primary-theme">Attach Workspace File</span>
          {/if}
        </div>
        <button
          onclick={onClose}
          class="p-1 text-secondary-theme hover:text-primary-theme rounded hover:bg-surface transition-colors cursor-pointer"
        >
          <X size={15} />
        </button>
      </div>

      <div class="p-3 border-b border-subtle flex items-center gap-2.5 bg-surface">
        <Search size={15} class="text-muted-theme shrink-0" />
        <input
          type="text"
          bind:this={searchInputEl}
          bind:value={search}
          placeholder={mode === "directory" ? "Search folders or type path... (e.g. src/components/)" : "Search files or type path... (e.g. src/main.rs)"}
          class="flex-1 bg-transparent text-primary-theme text-xs focus:outline-none placeholder:text-muted-theme"
        />
        {#if search.trim()}
          <button
            onclick={handleCustomSubmit}
            class="flex items-center gap-1 px-2 py-1 rounded bg-accent-subtle text-accent-theme hover:bg-accent-theme hover:text-white text-[11px] font-medium transition-colors cursor-pointer"
            title="Use typed path directly"
          >
            <span>Attach Path</span>
            <CornerDownLeft size={11} />
          </button>
        {/if}
      </div>

      <div class="flex-1 overflow-y-auto divide-y divide-subtle/40 p-1">
        {#if filteredEntries.length === 0}
          <div class="py-12 text-center text-xs text-muted-theme">
            {#if search.trim()}
              <p>No workspace {mode === "directory" ? "folders" : "files"} matching "{search}".</p>
              <button
                onclick={handleCustomSubmit}
                class="mt-3 inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-surface-elevated hover:bg-surface text-accent-theme border border-subtle text-xs cursor-pointer transition-colors"
              >
                <span>Attach as custom path: <code class="font-mono">{search.trim()}</code></span>
              </button>
            {:else}
              <p>No {mode === "directory" ? "folders" : "files"} found in this workspace.</p>
            {/if}
          </div>
        {:else}
          {#each filteredEntries as entry, idx (entry.relative_path)}
            <button
              class="w-full px-3.5 py-2.5 text-left flex items-center justify-between rounded-lg transition-colors cursor-pointer {idx === selectedIndex ? 'bg-accent-theme/15 text-accent-theme border border-accent-subtle' : 'hover:bg-surface-hover text-secondary-theme'}"
              onclick={() => chooseEntry(entry)}
              onmouseenter={() => (selectedIndex = idx)}
            >
              <div class="flex items-center gap-2.5 min-w-0 flex-1">
                {#if entry.is_dir}
                  <Folder size={15} class="text-sky-400 shrink-0" />
                {:else}
                  <File size={15} class="text-accent-theme shrink-0" />
                {/if}
                <div class="min-w-0 flex-1">
                  <div class="text-xs font-mono text-primary-theme truncate">
                    {entry.name}{entry.is_dir ? "/" : ""}
                  </div>
                  <div class="text-[10px] text-muted-theme truncate font-mono">
                    {entry.relative_path.replace(/\\/g, "/")}{entry.is_dir ? "/" : ""}
                  </div>
                </div>
              </div>
              <span class="text-[10px] text-muted-theme font-mono ml-2 shrink-0">
                @{entry.relative_path.replace(/\\/g, "/")}{entry.is_dir ? "/" : ""}
              </span>
            </button>
          {/each}
        {/if}
      </div>

      <div class="px-4 py-2 bg-surface-elevated/40 border-t border-subtle flex items-center justify-between text-[11px] text-muted-theme">
        <span class="font-mono text-[10px]">
          {filteredEntries.length} items &bull; Gemini CLI expands files via <span class="text-accent-theme">@path</span>
        </span>
        <div class="flex items-center gap-2 text-[10px]">
          <span>↑↓ Navigate</span>
          <span>&bull;</span>
          <span>Enter Select</span>
          <span>&bull;</span>
          <span>Esc Close</span>
        </div>
      </div>
    </div>
  </div>
{/if}
