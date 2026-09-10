<script lang="ts">
  import { THEMES, themeManager, type ThemeId } from "$lib/theme.svelte";
  import { X, Check, Palette } from "lucide-svelte";

  let {
    isOpen = false,
    onClose,
  }: {
    isOpen: boolean;
    onClose: () => void;
  } = $props();

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Escape" && isOpen) {
      onClose();
    }
  }
</script>

<svelte:window onkeydown={handleKeyDown} />

{#if isOpen}
  <!-- Backdrop -->
  <div
    role="presentation"
    class="fixed inset-0 bg-black/70 backdrop-blur-xs z-50 flex items-center justify-center p-4 select-none"
    onclick={onClose}
  >
    <!-- Modal Card -->
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="theme-modal-title"
      tabindex="-1"
      class="w-full max-w-md bg-surface border border-theme-default rounded-xl shadow-2xl overflow-hidden flex flex-col animate-in fade-in zoom-in-95 duration-150"
      onclick={(e) => e.stopPropagation()}
    >
      <!-- Header -->
      <div class="px-5 py-4 border-b border-subtle flex items-center justify-between">
        <div class="flex items-center gap-2.5">
          <div class="w-8 h-8 rounded-lg bg-accent-subtle border border-accent-subtle flex items-center justify-center text-accent-theme">
            <Palette size={18} />
          </div>
          <div>
            <h2 id="theme-modal-title" class="text-sm font-semibold text-primary-theme">Appearance & Theme</h2>
            <p class="text-xs text-secondary-theme">Select your preferred color scheme</p>
          </div>
        </div>
        <button
          onclick={onClose}
          aria-label="Close modal"
          class="p-1 rounded-lg text-secondary-theme hover:text-primary-theme hover:bg-surface-elevated transition-colors"
        >
          <X size={16} />
        </button>
      </div>

      <!-- Theme List -->
      <div class="p-4 space-y-2.5 max-h-[60vh] overflow-y-auto">
        {#each THEMES as theme}
          {@const isSelected = themeManager.current === theme.id}
          <button
            onclick={() => themeManager.setTheme(theme.id)}
            class="w-full p-3 rounded-lg border text-left flex items-center justify-between transition-all {isSelected ? 'border-accent-theme bg-accent-subtle shadow-xs' : 'border-subtle hover:border-theme-strong bg-surface-elevated/40 hover:bg-surface-elevated'}"
          >
            <div class="flex items-center gap-3">
              <!-- Swatch Preview Bubble -->
              <div
                class="w-9 h-9 rounded-lg flex items-center justify-center shadow-inner border relative overflow-hidden shrink-0"
                style="background-color: {theme.bgPreview}; border-color: {theme.borderPreview};"
              >
                <!-- Accent Dot / Bar -->
                <div
                  class="w-3.5 h-3.5 rounded-full shadow-sm"
                  style="background-color: {theme.accent};"
                ></div>
              </div>

              <div>
                <div class="flex items-center gap-2">
                  <span class="text-xs font-semibold text-primary-theme">{theme.name}</span>
                  {#if isSelected}
                    <span class="text-[10px] px-1.5 py-0.5 rounded font-medium bg-accent-theme text-white">
                      Active
                    </span>
                  {/if}
                </div>
                <p class="text-[11px] text-secondary-theme">{theme.tagline}</p>
              </div>
            </div>

            <!-- Checkmark Indicator -->
            <div class="shrink-0 pl-2">
              {#if isSelected}
                <div class="w-5 h-5 rounded-full bg-accent-theme flex items-center justify-center text-white">
                  <Check size={12} strokeWidth={3} />
                </div>
              {:else}
                <div class="w-5 h-5 rounded-full border border-subtle"></div>
              {/if}
            </div>
          </button>
        {/each}
      </div>

      <!-- Footer -->
      <div class="px-5 py-3 border-t border-subtle bg-surface-elevated/50 flex justify-end">
        <button
          onclick={onClose}
          class="px-4 py-1.5 rounded-lg bg-accent-theme hover:bg-accent-hover text-white text-xs font-medium transition-colors shadow-xs"
        >
          Done
        </button>
      </div>
    </div>
  </div>
{/if}
