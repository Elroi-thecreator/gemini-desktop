<script lang="ts">
  import type { PromptTemplate } from "$lib/types";
  import { X, BookOpen } from "lucide-svelte";

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
    class="fixed inset-0 bg-black/60 backdrop-blur-xs z-50 flex items-center justify-center p-4"
    onclick={onClose}
    role="presentation"
  >
    <div
      class="w-full max-w-2xl bg-surface border border-theme-default rounded-2xl shadow-2xl overflow-hidden flex flex-col max-h-[80vh]"
      onclick={(e) => e.stopPropagation()}
      role="dialog"
    >
      <!-- Header -->
      <div class="px-5 py-4 border-b border-subtle flex items-center justify-between bg-surface-elevated/40">
        <div class="flex items-center gap-2">
          <BookOpen size={18} class="text-accent-theme" />
          <h2 class="text-sm font-semibold text-primary-theme">Prompt Template Library</h2>
        </div>
        <button onclick={onClose} class="p-1 text-secondary-theme hover:text-primary-theme rounded">
          <X size={16} />
        </button>
      </div>

      <!-- Categories Filter -->
      <div class="px-5 py-2.5 border-b border-subtle flex items-center gap-1.5 overflow-x-auto text-xs">
        {#each categories as cat}
          <button
            onclick={() => (activeCategory = cat)}
            class="px-2.5 py-1 rounded-full transition-colors {activeCategory === cat ? 'bg-accent-theme text-white font-semibold shadow-xs' : 'bg-surface-elevated text-secondary-theme hover:text-primary-theme'}"
          >
            {cat}
          </button>
        {/each}
      </div>

      <!-- Templates Grid -->
      <div class="flex-1 overflow-y-auto p-5 grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
        {#each filteredTemplates as item}
          <div class="p-3.5 rounded-xl bg-app/60 border border-subtle hover:border-theme-strong flex flex-col justify-between transition-colors">
            <div>
              <div class="flex items-center justify-between mb-1.5">
                <span class="font-semibold text-primary-theme">{item.title}</span>
                <span class="text-[10px] px-1.5 py-0.5 rounded bg-surface-elevated text-accent-theme font-mono">{item.category}</span>
              </div>
              <p class="text-[11px] text-secondary-theme leading-relaxed line-clamp-3">
                {item.content}
              </p>
            </div>

            <div class="mt-3 pt-2.5 border-t border-subtle flex items-center justify-end">
              <button
                onclick={() => {
                  onSelectTemplate(item.content);
                  onClose();
                }}
                class="flex items-center gap-1 text-xs text-accent-theme hover:underline font-medium transition-colors"
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
