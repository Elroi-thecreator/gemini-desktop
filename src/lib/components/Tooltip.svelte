<script lang="ts">
  import type { Snippet } from "svelte";

  let {
    text = "",
    subtext = "",
    shortcut = "",
    position = "top",
    class: className = "",
    children,
  }: {
    text: string;
    subtext?: string;
    shortcut?: string;
    position?: "top" | "bottom" | "left" | "right";
    class?: string;
    children: Snippet;
  } = $props();

  let isHovered = $state(false);
</script>

<div
  class="relative {isHovered ? 'z-50' : ''} {className || 'inline-flex items-center'}"
  role="presentation"
  onmouseenter={() => (isHovered = true)}
  onmouseleave={() => (isHovered = false)}
>
  {@render children()}

  {#if isHovered && text}
    <div
      class="absolute z-50 pointer-events-none px-2.5 py-1.5 rounded-lg bg-surface-elevated/95 border border-theme-default text-primary-theme shadow-2xl backdrop-blur-md animate-in fade-in zoom-in-95 duration-100 whitespace-nowrap text-left
        {position === 'top' ? 'bottom-full mb-2 left-1/2 -translate-x-1/2' : ''}
        {position === 'bottom' ? 'top-full mt-2 left-1/2 -translate-x-1/2' : ''}
        {position === 'left' ? 'right-full mr-2 top-1/2 -translate-y-1/2' : ''}
        {position === 'right' ? 'left-full ml-2 top-1/2 -translate-y-1/2' : ''}"
      role="tooltip"
    >
      <div class="flex items-center gap-2 text-xs font-semibold">
        <span>{text}</span>
        {#if shortcut}
          <kbd class="text-[10px] font-mono px-1.5 py-0.5 rounded bg-surface border border-subtle text-accent-theme">
            {shortcut}
          </kbd>
        {/if}
      </div>
      {#if subtext}
        <div class="text-[11px] text-secondary-theme font-normal mt-0.5 max-w-xs whitespace-normal leading-tight">
          {subtext}
        </div>
      {/if}
    </div>
  {/if}
</div>
