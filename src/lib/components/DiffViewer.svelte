<script lang="ts">
  import { computeLineDiff, parseUnifiedPatch, type DiffResult, type DiffLine } from "$lib/diff";
  import { Copy, Check, Columns, AlignJustify, ChevronDown, ChevronUp, FileCode } from "lucide-svelte";
  import Tooltip from "$lib/components/Tooltip.svelte";

  interface Props {
    oldText?: string;
    newText?: string;
    patch?: string;
    filePath?: string;
    maxCollapsedLines?: number;
  }

  let {
    oldText = "",
    newText = "",
    patch = "",
    filePath = "",
    maxCollapsedLines = 30,
  }: Props = $props();

  let viewMode = $state<"unified" | "split">("unified");
  let isExpanded = $state(false);
  let isCopied = $state(false);

  // Compute diff reactively based on incoming props
  let diff = $derived.by<DiffResult>(() => {
    if (patch && patch.trim()) {
      return parseUnifiedPatch(patch, filePath);
    }
    return computeLineDiff(oldText, newText, filePath);
  });

  let totalDiffLines = $derived.by(() => {
    return diff.hunks.reduce((acc, h) => acc + h.lines.length, 0);
  });

  let shouldShowCollapseControl = $derived(totalDiffLines > maxCollapsedLines);

  interface SplitRow {
    type: "header" | "content";
    headerText?: string;
    left?: DiffLine;
    right?: DiffLine;
  }

  // Pre-calculate side-by-side split rows for split mode
  let splitRows = $derived.by<SplitRow[]>(() => {
    const rows: SplitRow[] = [];

    for (const hunk of diff.hunks) {
      if (hunk.header) {
        rows.push({ type: "header", headerText: hunk.header });
      }

      let i = 0;
      while (i < hunk.lines.length) {
        const line = hunk.lines[i];

        if (line.type === "same") {
          rows.push({
            type: "content",
            left: line,
            right: line,
          });
          i++;
        } else {
          // Collect consecutive deletions and additions to align side-by-side
          const dels: DiffLine[] = [];
          const adds: DiffLine[] = [];

          while (i < hunk.lines.length && hunk.lines[i].type === "del") {
            dels.push(hunk.lines[i]);
            i++;
          }
          while (i < hunk.lines.length && hunk.lines[i].type === "add") {
            adds.push(hunk.lines[i]);
            i++;
          }

          const maxLen = Math.max(dels.length, adds.length);
          for (let k = 0; k < maxLen; k++) {
            rows.push({
              type: "content",
              left: dels[k],
              right: adds[k],
            });
          }
        }
      }
    }

    return rows;
  });

  async function handleCopy() {
    const textToCopy = newText || patch || (diff.hunks.map(h => h.lines.map(l => l.content).join("\n")).join("\n"));
    try {
      await navigator.clipboard.writeText(textToCopy);
      isCopied = true;
      setTimeout(() => (isCopied = false), 1500);
    } catch (e) {
      console.warn("Failed to copy code:", e);
    }
  }
</script>

<div class="diff-viewer rounded-xl border border-subtle bg-app overflow-hidden font-mono text-xs shadow-inner">
  <!-- Diff Toolbar Header -->
  <div class="flex items-center justify-between px-3.5 py-2 bg-surface-elevated/70 border-b border-subtle select-none">
    <div class="flex items-center gap-2 truncate">
      <FileCode size={14} class="text-accent-theme shrink-0" />
      <span class="font-mono text-xs font-semibold text-primary-theme truncate" title={diff.fileName || filePath || "File Diff"}>
        {diff.fileName || filePath || "Modified File"}
      </span>
      <div class="flex items-center gap-1.5 ml-2 text-[11px] font-sans">
        {#if diff.additions > 0}
          <span class="px-1.5 py-0.2 rounded font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">
            +{diff.additions}
          </span>
        {/if}
        {#if diff.deletions > 0}
          <span class="px-1.5 py-0.2 rounded font-semibold bg-rose-500/15 text-rose-400 border border-rose-500/30">
            -{diff.deletions}
          </span>
        {/if}
        {#if diff.additions === 0 && diff.deletions === 0}
          <span class="px-1.5 py-0.2 rounded text-[10px] text-muted-theme bg-surface border border-subtle">
            no changes
          </span>
        {/if}
      </div>
    </div>

    <!-- Actions / View Mode Switcher -->
    <div class="flex items-center gap-1.5">
      <!-- Unified vs Split Switch -->
      <div class="inline-flex rounded-lg border border-theme-default bg-surface p-0.5 text-[11px]">
        <button
          type="button"
          onclick={() => (viewMode = "unified")}
          class="flex items-center gap-1 px-2 py-0.5 rounded transition-colors cursor-pointer {viewMode === 'unified' ? 'bg-accent-theme text-white font-medium shadow-2xs' : 'text-secondary-theme hover:text-primary-theme'}"
          title="Unified Diff View"
        >
          <AlignJustify size={12} />
          <span>Unified</span>
        </button>
        <button
          type="button"
          onclick={() => (viewMode = "split")}
          class="flex items-center gap-1 px-2 py-0.5 rounded transition-colors cursor-pointer {viewMode === 'split' ? 'bg-accent-theme text-white font-medium shadow-2xs' : 'text-secondary-theme hover:text-primary-theme'}"
          title="Side-by-Side (Split) View"
        >
          <Columns size={12} />
          <span>Split</span>
        </button>
      </div>

      <!-- Copy Action -->
      <Tooltip text={isCopied ? "Copied to clipboard!" : "Copy code"} position="top">
        <button
          type="button"
          onclick={handleCopy}
          class="p-1.5 rounded-lg border border-theme-default bg-surface hover:bg-surface-hover text-secondary-theme hover:text-primary-theme transition-colors cursor-pointer flex items-center justify-center"
          aria-label="Copy code"
        >
          {#if isCopied}
            <Check size={13} class="text-emerald-400" />
          {:else}
            <Copy size={13} />
          {/if}
        </button>
      </Tooltip>
    </div>
  </div>

  <!-- Diff Content Body -->
  <div class="overflow-x-auto {isExpanded || !shouldShowCollapseControl ? 'max-h-96' : 'max-h-60'} overflow-y-auto transition-all duration-200">
    {#if viewMode === "unified"}
      <!-- UNIFIED VIEW -->
      <table class="w-full border-collapse font-mono text-[11px] leading-relaxed">
        <tbody>
          {#each diff.hunks as hunk, hunkIdx}
            {#if hunk.header}
              <tr class="bg-accent-subtle/40 text-accent-theme select-none border-y border-accent-subtle/50">
                <td colspan="3" class="px-3 py-1 font-semibold text-[10px] text-accent-theme/90">
                  {hunk.header}
                </td>
              </tr>
            {/if}

            {#each hunk.lines as line, lineIdx}
              <tr class="group transition-colors {
                line.type === 'add'
                  ? 'bg-emerald-500/10 text-emerald-300 dark:text-emerald-200'
                  : line.type === 'del'
                  ? 'bg-rose-500/10 text-rose-300 dark:text-rose-200'
                  : 'text-primary-theme hover:bg-surface/30'
              }">
                <!-- Line number Old -->
                <td class="w-10 px-2 py-0.5 text-right select-none text-[10px] text-muted-theme border-r border-subtle/30 opacity-60">
                  {line.oldLineNumber || ""}
                </td>
                <!-- Line number New -->
                <td class="w-10 px-2 py-0.5 text-right select-none text-[10px] text-muted-theme border-r border-subtle/30 opacity-60">
                  {line.newLineNumber || ""}
                </td>
                <!-- Diff Marker + Code Content -->
                <td class="px-2.5 py-0.5 whitespace-pre font-mono">
                  <span class="inline-block w-4 select-none font-bold {
                    line.type === 'add' ? 'text-emerald-400' : line.type === 'del' ? 'text-rose-400' : 'text-transparent'
                  }">
                    {line.type === 'add' ? '+' : line.type === 'del' ? '-' : ' '}
                  </span>
                  <span>{line.content}</span>
                </td>
              </tr>
            {/each}
          {/each}
        </tbody>
      </table>
    {:else}
      <!-- SPLIT (SIDE-BY-SIDE) VIEW -->
      <table class="w-full border-collapse font-mono text-[11px] leading-relaxed">
        <thead>
          <tr class="bg-surface/60 text-[10px] uppercase text-muted-theme border-b border-subtle select-none">
            <th class="w-10 px-2 py-1 text-right font-normal border-r border-subtle/40">Old</th>
            <th class="w-1/2 px-2.5 py-1 text-left font-semibold border-r border-theme-default">Original</th>
            <th class="w-10 px-2 py-1 text-right font-normal border-r border-subtle/40">New</th>
            <th class="w-1/2 px-2.5 py-1 text-left font-semibold">Modified</th>
          </tr>
        </thead>
        <tbody>
          {#each splitRows as row, rIdx}
            {#if row.type === "header"}
              <tr class="bg-accent-subtle/40 text-accent-theme select-none border-y border-accent-subtle/50">
                <td colspan="4" class="px-3 py-1 font-semibold text-[10px] text-accent-theme/90">
                  {row.headerText}
                </td>
              </tr>
            {:else}
              <tr class="border-b border-subtle/20 group">
                <!-- LEFT SIDE (Original / Deleted) -->
                <td class="w-10 px-2 py-0.5 text-right select-none text-[10px] text-muted-theme border-r border-subtle/40 opacity-60 {row.left?.type === 'del' ? 'bg-rose-500/15 text-rose-300' : ''}">
                  {row.left?.oldLineNumber || ""}
                </td>
                <td class="w-1/2 px-2 py-0.5 whitespace-pre font-mono border-r border-theme-default {
                  row.left?.type === 'del' ? 'bg-rose-500/10 text-rose-300 dark:text-rose-200' : 'text-primary-theme'
                }">
                  {#if row.left}
                    <span class="inline-block w-3 select-none font-bold {row.left.type === 'del' ? 'text-rose-400' : 'text-transparent'}">
                      {row.left.type === 'del' ? '-' : ' '}
                    </span>
                    <span>{row.left.content}</span>
                  {/if}
                </td>

                <!-- RIGHT SIDE (Modified / Added) -->
                <td class="w-10 px-2 py-0.5 text-right select-none text-[10px] text-muted-theme border-r border-subtle/40 opacity-60 {row.right?.type === 'add' ? 'bg-emerald-500/15 text-emerald-300' : ''}">
                  {row.right?.newLineNumber || ""}
                </td>
                <td class="w-1/2 px-2 py-0.5 whitespace-pre font-mono {
                  row.right?.type === 'add' ? 'bg-emerald-500/10 text-emerald-300 dark:text-emerald-200' : 'text-primary-theme'
                }">
                  {#if row.right}
                    <span class="inline-block w-3 select-none font-bold {row.right.type === 'add' ? 'text-emerald-400' : 'text-transparent'}">
                      {row.right.type === 'add' ? '+' : ' '}
                    </span>
                    <span>{row.right.content}</span>
                  {/if}
                </td>
              </tr>
            {/if}
          {/each}
        </tbody>
      </table>
    {/if}
  </div>

  <!-- Expand / Collapse Footer if Diff is Long -->
  {#if shouldShowCollapseControl}
    <div class="px-3 py-1.5 bg-surface-elevated/50 border-t border-subtle flex items-center justify-between text-[11px] text-muted-theme select-none">
      <span>Showing {isExpanded ? totalDiffLines : maxCollapsedLines} of {totalDiffLines} lines</span>
      <button
        type="button"
        onclick={() => (isExpanded = !isExpanded)}
        class="flex items-center gap-1 text-accent-theme hover:underline font-medium cursor-pointer"
      >
        {#if isExpanded}
          <span>Collapse diff</span>
          <ChevronUp size={12} />
        {:else}
          <span>Show all {totalDiffLines} lines</span>
          <ChevronDown size={12} />
        {/if}
      </button>
    </div>
  {/if}
</div>
