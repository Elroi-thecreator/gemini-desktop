<script lang="ts">
  import type { PromptTemplate } from "$lib/types";
  import { X, Sparkles, Copy, Check, BookOpen } from "lucide-svelte";

  let {
    isOpen = false,
    templates = [],
    onClose,
    onSelectTemplate,
  }: {
    isOpen: boolean;
    templates: PromptTemplate[];
    onClose: () => void;
    onSelectTemplate: (text: string) => void;
  } = $props();

  let activeCategory = $state("All");
  let categories = $derived(["All", ...new Set(templates.map((t) => t.category))]);
  let filteredTemplates = $derived(
    activeCategory === "All"
      ? templates
      : templates.filter((t) => t.category === activeCategory)
  );
</script>

{#if isOpen}
  <div
    class="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
    onclick={onClose}
    role="presentation"
  >
    <div
      class="w-full max-w-2xl bg-slate-900 border border-slate-700 rounded-2xl shadow-2xl overflow-hidden flex flex-col max-h-[80vh]"
      onclick={(e) => e.stopPropagation()}
      role="dialog"
    >
      <!-- Header -->
      <div class="px-5 py-4 border-b border-slate-800 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <BookOpen size={18} class="text-sky-400" />
          <h2 class="text-sm font-semibold text-slate-100">Prompt Template Library</h2>
        </div>
        <button onclick={onClose} class="p-1 text-slate-400 hover:text-white rounded">
          <X size={16} />
        </button>
      </div>

      <!-- Categories Filter -->
      <div class="px-5 py-2.5 border-b border-slate-800 flex items-center gap-1.5 overflow-x-auto text-xs">
        {#each categories as cat}
          <button
            onclick={() => (activeCategory = cat)}
            class="px-2.5 py-1 rounded-full transition-colors {activeCategory === cat ? 'bg-sky-500 text-slate-950 font-semibold' : 'bg-slate-800 text-slate-400 hover:text-slate-200'}"
          >
            {cat}
          </button>
        {/each}
      </div>

      <!-- Templates Grid -->
      <div class="flex-1 overflow-y-auto p-5 grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
        {#each filteredTemplates as item}
          <div class="p-3.5 rounded-xl bg-slate-950/60 border border-slate-800 hover:border-slate-700 flex flex-col justify-between transition-colors">
            <div>
              <div class="flex items-center justify-between mb-1.5">
                <span class="font-semibold text-slate-200">{item.title}</span>
                <span class="text-[10px] px-1.5 py-0.5 rounded bg-slate-800 text-sky-400 font-mono">{item.category}</span>
              </div>
              <p class="text-[11px] text-slate-400 leading-relaxed line-clamp-3">
                {item.content}
              </p>
            </div>

            <div class="mt-3 pt-2.5 border-t border-slate-800/80 flex items-center justify-end">
              <button
                onclick={() => {
                  onSelectTemplate(item.content);
                  onClose();
                }}
                class="flex items-center gap-1 text-xs text-sky-400 hover:text-sky-300 font-medium transition-colors"
              >
                <span>Use Prompt</span>
                &rarr;
              </button>
            </div>
          </div>
        {/each}
      </div>
    </div>
  </div>
{/if}
