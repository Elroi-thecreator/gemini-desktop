<script lang="ts">
  import type { SearchResult } from "$lib/types";
  import { Search, X, MessageSquare, ArrowRight } from "lucide-svelte";

  let {
    isOpen = false,
    onClose,
    onSearch,
    onSelectResult,
  }: {
    isOpen: boolean;
    onClose: () => void;
    onSearch: (query: string) => Promise<SearchResult[]>;
    onSelectResult: (result: SearchResult) => void;
  } = $props();

  let query = $state("");
  let results: SearchResult[] = $state([]);
  let isSearching = $state(false);

  async function handleInput() {
    if (!query.trim()) {
      results = [];
      return;
    }
    isSearching = true;
    try {
      results = await onSearch(query.trim());
    } finally {
      isSearching = false;
    }
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Escape") {
      onClose();
    }
  }
</script>

{#if isOpen}
  <div
    class="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-start justify-center pt-24 p-4"
    onclick={onClose}
    onkeydown={handleKeyDown}
    role="presentation"
  >
    <!-- Modal Window -->
    <div
      class="w-full max-w-xl bg-slate-900 border border-slate-700 rounded-2xl shadow-2xl overflow-hidden flex flex-col max-h-[70vh]"
      onclick={(e) => e.stopPropagation()}
      role="dialog"
    >
      <!-- Search Input Header -->
      <div class="p-3 border-b border-slate-800 flex items-center gap-2.5">
        <Search size={16} class="text-sky-400 shrink-0" />
        <input
          type="text"
          bind:value={query}
          oninput={handleInput}
          placeholder="Search all conversations via SQLite FTS5... (Esc to close)"
          class="flex-1 bg-transparent text-slate-100 text-xs focus:outline-none placeholder-slate-500"
          autofocus
        />
        <button onclick={onClose} class="p-1 text-slate-400 hover:text-white rounded">
          <X size={16} />
        </button>
      </div>

      <!-- Search Results List -->
      <div class="flex-1 overflow-y-auto p-2 space-y-1">
        {#if results.length === 0}
          <div class="py-12 text-center text-xs text-slate-500">
            {#if query.trim()}
              {isSearching ? "Searching..." : "No messages matched your query."}
            {:else}
              Type anything to instantly search across historical conversations.
            {/if}
          </div>
        {:else}
          {#each results as res}
            <button
              onclick={() => {
                onSelectResult(res);
                onClose();
              }}
              class="w-full text-left p-3 rounded-xl hover:bg-slate-800 border border-transparent hover:border-slate-700/60 transition-colors group"
            >
              <div class="flex items-center justify-between text-[11px] text-slate-400 mb-1">
                <span class="flex items-center gap-1.5 font-medium text-sky-400">
                  <MessageSquare size={12} />
                  {res.session_title}
                </span>
                <span class="text-slate-500 uppercase">{res.role}</span>
              </div>
              <div class="text-xs text-slate-200 line-clamp-2 select-none">
                {@html res.snippet}
              </div>
            </button>
          {/each}
        {/if}
      </div>
    </div>
  </div>
{/if}
