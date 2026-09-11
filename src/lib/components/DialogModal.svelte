<script lang="ts">
  import { dialogManager } from "$lib/dialog.svelte";
  import { AlertCircle, HelpCircle, Info, Sparkles, X, Check } from "lucide-svelte";

  let inputEl = $state<HTMLInputElement | null>(null);

  $effect(() => {
    if (dialogManager.isOpen && dialogManager.type === "prompt") {
      setTimeout(() => inputEl?.focus(), 50);
    }
  });

  function handleKeydown(e: KeyboardEvent) {
    if (!dialogManager.isOpen) return;

    if (e.key === "Escape") {
      e.preventDefault();
      dialogManager.handleCancel();
    } else if (e.key === "Enter" && dialogManager.type !== "prompt") {
      e.preventDefault();
      dialogManager.handleConfirm();
    }
  }

  function handlePromptKeydown(e: KeyboardEvent) {
    if (e.key === "Enter") {
      e.preventDefault();
      dialogManager.handleConfirm();
    }
  }
</script>

<svelte:window onkeydown={handleKeydown} />

{#if dialogManager.isOpen}
  <div
    class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs animate-in fade-in duration-150"
    onclick={() => dialogManager.handleCancel()}
    role="presentation"
  >
    <div
      class="w-full max-w-md bg-surface border border-theme-default rounded-2xl shadow-2xl overflow-hidden flex flex-col animate-in zoom-in-95 duration-150"
      onclick={(e) => e.stopPropagation()}
      role="dialog"
      aria-modal="true"
      tabindex="-1"
    >
      <!-- Header -->
      <div class="px-5 py-3.5 border-b border-subtle flex items-center justify-between bg-surface-elevated/50">
        <div class="flex items-center gap-2.5">
          <div class="p-1.5 rounded-lg {dialogManager.isDestructive ? 'bg-rose-500/10 text-rose-400' : 'bg-accent-subtle text-accent-theme'}">
            {#if dialogManager.isDestructive}
              <AlertCircle size={16} />
            {:else if dialogManager.type === 'confirm'}
              <HelpCircle size={16} />
            {:else if dialogManager.type === 'prompt'}
              <Sparkles size={16} />
            {:else}
              <Info size={16} />
            {/if}
          </div>
          <div>
            <h3 class="text-sm font-semibold text-primary-theme flex items-center gap-2">
              {dialogManager.title}
            </h3>
            <span class="text-[10px] text-muted-theme font-medium">Gemini Desktop</span>
          </div>
        </div>

        <button
          onclick={() => dialogManager.handleCancel()}
          class="p-1 text-secondary-theme hover:text-primary-theme hover:bg-surface rounded-lg transition-colors cursor-pointer"
          aria-label="Close dialog"
        >
          <X size={15} />
        </button>
      </div>

      <!-- Body -->
      <div class="p-5 space-y-4">
        <p class="text-xs text-secondary-theme leading-relaxed whitespace-pre-wrap">
          {dialogManager.message}
        </p>

        {#if dialogManager.type === "prompt"}
          <div>
            <input
              bind:this={inputEl}
              bind:value={dialogManager.inputValue}
              onkeydown={handlePromptKeydown}
              placeholder={dialogManager.placeholder}
              class="w-full px-3 py-2 bg-app border border-theme-default rounded-lg text-primary-theme text-xs focus:outline-none focus:border-accent-theme"
            />
          </div>
        {/if}
      </div>

      <!-- Footer Buttons -->
      <div class="px-5 py-3 border-t border-subtle bg-surface-elevated/30 flex items-center justify-end gap-2 text-xs">
        {#if dialogManager.type !== "alert"}
          <button
            type="button"
            onclick={() => dialogManager.handleCancel()}
            class="px-3 py-1.5 rounded-lg text-secondary-theme hover:text-primary-theme hover:bg-surface transition-colors cursor-pointer"
          >
            {dialogManager.cancelText}
          </button>
        {/if}

        <button
          type="button"
          onclick={() => dialogManager.handleConfirm()}
          class="flex items-center gap-1.5 px-4 py-1.5 rounded-lg font-medium text-white transition-colors shadow-xs cursor-pointer {dialogManager.isDestructive ? 'bg-rose-500 hover:bg-rose-600' : 'bg-accent-theme hover:bg-accent-hover'}"
        >
          {#if !dialogManager.isDestructive}
            <Check size={13} />
          {/if}
          <span>{dialogManager.confirmText}</span>
        </button>
      </div>
    </div>
  </div>
{/if}
