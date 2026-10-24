<script lang="ts">
  import type { Session, Message, Workspace } from "$lib/types";
  import { modalManager } from "$lib/modal.svelte";
  import { dialogManager } from "$lib/dialog.svelte";
  import { RotateCcw, History, Clock, X, CheckCircle2, AlertTriangle, FileCode2, MessageSquare } from "lucide-svelte";
  import { invoke } from "@tauri-apps/api/core";

  let {
    workspace = null,
    session = null,
    messages = [],
    selectedMessageId = null,
    onRewound,
  }: {
    workspace: Workspace | null;
    session: Session | null;
    messages: Message[];
    selectedMessageId?: string | null;
    onRewound: (targetMessageId: string, rewoundCount: number, restoredFiles: string[], prevPrompt?: string | null) => void;
  } = $props();

  let targetId = $state<string | null>(null);
  let revertMode = $state<"both" | "conversation" | "code">("both");
  let isRewinding = $state(false);
  let modalCardElem: HTMLDivElement | null = $state(null);

  // Group messages into user prompt turns
  let turns = $derived(
    messages
      .filter((m) => m.role === "user")
      .map((userMsg, idx, arr) => {
        const assistantReplies = messages.filter(
          (m) =>
            m.role === "assistant" &&
            m.created_at >= userMsg.created_at &&
            (idx === arr.length - 1 || m.created_at < arr[idx + 1].created_at)
        );
        const hasTools = assistantReplies.some(
          (a) => a.tool_calls_json && a.tool_calls_json !== "[]"
        );
        return {
          id: userMsg.id,
          created_at: userMsg.created_at,
          snippet:
            userMsg.content.slice(0, 140) + (userMsg.content.length > 140 ? "..." : ""),
          fullContent: userMsg.content,
          replyCount: assistantReplies.length,
          hasTools,
          turnNumber: idx + 1,
        };
      })
      .reverse()
  );

  let selectedTurn = $derived(turns.find((t) => t.id === targetId) ?? null);
  let selectedTurnIndex = $derived(turns.findIndex((t) => t.id === targetId));
  // turns is reversed, so index 0 is latest turn. Turns to rewind are from index 0 to selectedTurnIndex.
  let turnsToRemove = $derived(
    selectedTurnIndex >= 0 ? turns.slice(0, selectedTurnIndex + 1) : []
  );
  let turnsToPreserve = $derived(
    selectedTurnIndex >= 0 ? turns.slice(selectedTurnIndex + 1) : []
  );

  $effect(() => {
    if (modalManager.isOpen("rewind")) {
      if (selectedMessageId) {
        let match = turns.find((t) => t.id === selectedMessageId);
        if (!match) {
          // If selectedMessageId was an assistant message, find the user turn that preceded it
          const selMsg = messages.find((m) => m.id === selectedMessageId);
          if (selMsg) {
            match = turns.find((t) => t.created_at <= selMsg.created_at);
          }
        }
        targetId = match ? match.id : (turns[0]?.id ?? null);
      } else if (turns.length > 0) {
        targetId = turns[0]?.id ?? null;
      }
      setTimeout(() => {
        modalCardElem?.focus();
      }, 50);
    }
  });

  function close() {
    modalManager.close();
  }

  async function handleRewind() {
    if (!targetId || !session) return;
    isRewinding = true;

    try {
      const res = await invoke<{
        rewound_messages_count: number;
        restored_files: string[];
        previous_prompt: string | null;
      }>("rewind_turn", {
        sessionId: session.id,
        workspaceId: workspace?.id ?? "",
        targetMessageId: targetId,
        revertCode: revertMode === "both" || revertMode === "code",
        revertMessages: revertMode === "both" || revertMode === "conversation",
      });

      close();
      onRewound(targetId, res.rewound_messages_count, res.restored_files, res.previous_prompt);
    } catch (e: any) {
      await dialogManager.alert(e?.toString() || "Failed to rewind session", "Rewind Failed");
    } finally {
      isRewinding = false;
    }
  }

  function formatTime(iso: string) {
    try {
      const d = new Date(iso);
      return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    } catch {
      return iso;
    }
  }
</script>

{#if modalManager.isOpen("rewind")}
  <div
    class="fixed inset-0 bg-black/60 backdrop-blur-xs flex items-center justify-center z-50 p-4"
    role="presentation"
    onclick={close}
    onkeydown={(e) => e.key === "Escape" && close()}
  >
    <div
      bind:this={modalCardElem}
      class="bg-surface-elevated border border-subtle rounded-2xl w-full max-w-xl shadow-2xl overflow-hidden flex flex-col max-h-[88vh] text-primary-theme focus:outline-none"
      role="dialog"
      aria-modal="true"
      tabindex="-1"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => e.stopPropagation()}
    >
      <!-- Header -->
      <div class="px-6 py-4 border-b border-subtle flex items-center justify-between bg-surface/50">
        <div class="flex items-center gap-2.5">
          <div class="w-8 h-8 rounded-lg bg-accent-subtle text-accent-theme flex items-center justify-center">
            <History size={16} />
          </div>
          <div>
            <h3 class="font-semibold text-sm">Rewind Conversation & Checkpoint Restore</h3>
            <p class="text-[11px] text-secondary-theme">Select a prompt turn to rewind and rollback state</p>
          </div>
        </div>
        <button
          onclick={close}
          class="p-1 rounded-lg text-muted-theme hover:text-primary-theme hover:bg-surface-hover transition-colors cursor-pointer"
          aria-label="Close rewind dialog"
        >
          <X size={16} />
        </button>
      </div>

      <!-- Turn Selection List -->
      <div class="flex-1 overflow-y-auto p-4 space-y-2">
        {#if turns.length === 0}
          <div class="text-center py-10 text-muted-theme text-xs">
            No turns available to rewind in this session.
          </div>
        {:else}
          <div class="flex items-center justify-between text-[11px] font-medium text-secondary-theme px-1 mb-1">
            <span>Select turn to rewind (newest at top):</span>
            <span class="text-muted-theme font-mono text-[10px]">Total Turns: {turns.length}</span>
          </div>
          {#each turns as turn, idx (turn.id)}
            {@const isSelected = targetId === turn.id}
            {@const willBeRemoved = turnsToRemove.some(t => t.id === turn.id)}
            <button
              type="button"
              onclick={() => (targetId = turn.id)}
              class="w-full text-left p-3 rounded-xl border transition-all cursor-pointer flex flex-col gap-1.5 {isSelected ? 'bg-accent-subtle/40 border-accent-theme/60 shadow-xs ring-1 ring-accent-theme/40' : willBeRemoved ? 'bg-rose-500/5 border-rose-500/20' : 'bg-surface hover:bg-surface-hover border-subtle'}"
            >
              <div class="flex items-center justify-between text-[11px]">
                <div class="flex items-center gap-2">
                  <span class="w-5 h-5 rounded-full bg-surface-elevated text-accent-theme text-[10px] font-mono flex items-center justify-center border border-subtle">
                    #{turn.turnNumber}
                  </span>
                  <span class="font-semibold text-primary-theme">
                    Prompt Turn
                  </span>
                  {#if idx === 0}
                    <span class="px-1.5 py-0.5 rounded text-[9px] font-medium bg-accent-subtle text-accent-theme">Latest</span>
                  {/if}
                  {#if isSelected}
                    <span class="px-1.5 py-0.5 rounded text-[9px] font-medium bg-amber-500/20 text-amber-300">Selected Target</span>
                  {/if}
                </div>
                <span class="text-muted-theme flex items-center gap-1 font-mono text-[10px]">
                  <Clock size={11} />
                  {formatTime(turn.created_at)}
                </span>
              </div>
              <div class="text-xs text-secondary-theme font-sans line-clamp-2 pl-7">
                "{turn.snippet}"
              </div>
              <div class="flex items-center gap-3 pl-7 text-[10px] text-muted-theme">
                <span>{turn.replyCount} response message(s)</span>
                {#if turn.hasTools}
                  <span class="text-amber-400/90 font-medium">&bull; Tools executed</span>
                {/if}
                {#if willBeRemoved}
                  <span class="text-rose-400 font-medium ml-auto">&bull; Will be rewound</span>
                {:else}
                  <span class="text-emerald-400 font-medium ml-auto">&bull; Will be preserved</span>
                {/if}
              </div>
            </button>
          {/each}
        {/if}

        <!-- Rewind Scope Live Preview -->
        {#if selectedTurn}
          <div class="rounded-xl border border-subtle bg-surface/50 p-3.5 space-y-2 text-xs mt-3">
            <div class="flex items-center justify-between font-medium">
              <span class="text-secondary-theme">Turn Isolation Preview:</span>
              <span class="text-accent-theme font-semibold text-[11px]">
                {turnsToRemove.length} turn(s) rewound &bull; {turnsToPreserve.length} turn(s) kept
              </span>
            </div>
            <div class="grid grid-cols-2 gap-2 pt-0.5">
              <div class="p-2.5 rounded-lg bg-rose-500/10 border border-rose-500/20 text-rose-300">
                <div class="font-medium text-[11px] mb-1 flex items-center gap-1">
                  <span>🗑️ Removing ({turnsToRemove.length}):</span>
                </div>
                <ul class="space-y-0.5 text-[10px] text-rose-200/80">
                  {#each turnsToRemove as t}
                    <li class="truncate">&bull; Turn #{t.turnNumber}: "{t.snippet}"</li>
                  {/each}
                </ul>
              </div>
              <div class="p-2.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-emerald-300">
                <div class="font-medium text-[11px] mb-1 flex items-center gap-1">
                  <span>🛡️ Preserving ({turnsToPreserve.length}):</span>
                </div>
                {#if turnsToPreserve.length === 0}
                  <div class="text-[10px] text-emerald-200/60 italic">Resetting to initial conversation state</div>
                {:else}
                  <ul class="space-y-0.5 text-[10px] text-emerald-200/80">
                    {#each turnsToPreserve as t}
                      <li class="truncate">&bull; Turn #{t.turnNumber}: "{t.snippet}"</li>
                    {/each}
                  </ul>
                {/if}
              </div>
            </div>
          </div>
        {/if}
      </div>

      <!-- Official Gemini CLI 3-Way Revert Options & Actions -->
      <div class="p-4 border-t border-subtle bg-surface/30 space-y-3">
        <div class="space-y-1.5">
          <div class="text-[11px] font-semibold text-secondary-theme">Rewind Action Mode:</div>
          <div class="grid grid-cols-3 gap-2">
            <button
              type="button"
              onclick={() => (revertMode = "both")}
              class="p-2 rounded-lg border text-left transition-all cursor-pointer flex flex-col gap-1 {revertMode === 'both' ? 'bg-accent-subtle/50 border-accent-theme text-primary-theme ring-1 ring-accent-theme/40' : 'bg-surface border-subtle text-secondary-theme hover:bg-surface-hover'}"
            >
              <div class="flex items-center gap-1.5 font-medium text-[11px]">
                <RotateCcw size={12} class="text-rose-400" />
                <span>Chat & Code</span>
              </div>
              <span class="text-[9px] text-muted-theme leading-tight">Reverts both conversation & selective file edits</span>
            </button>

            <button
              type="button"
              onclick={() => (revertMode = "conversation")}
              class="p-2 rounded-lg border text-left transition-all cursor-pointer flex flex-col gap-1 {revertMode === 'conversation' ? 'bg-accent-subtle/50 border-accent-theme text-primary-theme ring-1 ring-accent-theme/40' : 'bg-surface border-subtle text-secondary-theme hover:bg-surface-hover'}"
            >
              <div class="flex items-center gap-1.5 font-medium text-[11px]">
                <MessageSquare size={12} class="text-accent-theme" />
                <span>Chat Only</span>
              </div>
              <span class="text-[9px] text-muted-theme leading-tight">Rewinds prompt history; leaves files on disk intact</span>
            </button>

            <button
              type="button"
              onclick={() => (revertMode = "code")}
              class="p-2 rounded-lg border text-left transition-all cursor-pointer flex flex-col gap-1 {revertMode === 'code' ? 'bg-accent-subtle/50 border-accent-theme text-primary-theme ring-1 ring-accent-theme/40' : 'bg-surface border-subtle text-secondary-theme hover:bg-surface-hover'}"
            >
              <div class="flex items-center gap-1.5 font-medium text-[11px]">
                <FileCode2 size={12} class="text-amber-400" />
                <span>Code Only</span>
              </div>
              <span class="text-[9px] text-muted-theme leading-tight">Reverts selective files; keeps full chat context</span>
            </button>
          </div>
        </div>

        <div class="flex items-center justify-between pt-1">
          <div class="text-[10px] text-muted-theme">
            {#if revertMode !== "conversation"}
              <span>* Selective rollback: Earlier untracked files (e.g. Turn #1) are preserved.</span>
            {:else}
              <span>* Files on disk will remain untouched.</span>
            {/if}
          </div>

          <div class="flex items-center gap-2">
            <button
              type="button"
              onclick={close}
              disabled={isRewinding}
              class="px-3.5 py-1.5 rounded-lg border border-subtle text-secondary-theme hover:text-primary-theme hover:bg-surface transition-colors text-xs font-medium cursor-pointer"
            >
              Cancel
            </button>

            <button
              type="button"
              onclick={handleRewind}
              disabled={!targetId || isRewinding}
              class="flex items-center gap-1.5 px-4 py-1.5 rounded-lg {revertMode === 'both' ? 'bg-rose-600 hover:bg-rose-500' : 'bg-accent-theme hover:bg-accent-hover'} text-white text-xs font-semibold transition-colors shadow-xs cursor-pointer disabled:opacity-40"
            >
              <RotateCcw size={13} />
              <span>
                {#if isRewinding}
                  Rewinding...
                {:else if revertMode === 'both'}
                  Rewind Turn #{selectedTurn?.turnNumber ?? ""} & Revert Code
                {:else if revertMode === 'conversation'}
                  Rewind Turn #{selectedTurn?.turnNumber ?? ""} (Chat Only)
                {:else}
                  Revert Turn #{selectedTurn?.turnNumber ?? ""} Code Only
                {/if}
              </span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
{/if}
