<script lang="ts">
  import type { Workspace } from "$lib/types";
  import { X, Folder, Plus, Trash2, Check } from "lucide-svelte";

  interface ModelOptionGroup {
    group: string;
    options: { value: string; label: string }[];
  }

  const MODEL_GROUPS: ModelOptionGroup[] = [
    {
      group: "Auto (Recommended by Gemini CLI)",
      options: [
        { value: "auto", label: "Auto (Gemini 3) — Auto-selects 3 Pro / 3 Flash" },
        { value: "auto-gemini-2.5", label: "Auto (Gemini 2.5) — Auto-selects 2.5 Pro / 2.5 Flash" },
      ],
    },
    {
      group: "Gemini 3",
      options: [
        { value: "gemini-3-pro-preview", label: "gemini-3-pro-preview (Deep reasoning, complex tasks & coding)" },
        { value: "gemini-3-flash-preview", label: "gemini-3-flash-preview (High speed, fast results)" },
      ],
    },
    {
      group: "Gemini 2.5",
      options: [
        { value: "gemini-2.5-pro", label: "gemini-2.5-pro (Complex tasks, coding & architecture)" },
        { value: "gemini-2.5-flash", label: "gemini-2.5-flash (Balanced speed & performance)" },
        { value: "gemini-2.5-flash-lite", label: "gemini-2.5-flash-lite (Ultra-fast & lightweight)" },
      ],
    },
    {
      group: "Gemini 1.5 (Legacy)",
      options: [
        { value: "gemini-1.5-pro", label: "gemini-1.5-pro (Legacy reasoning)" },
        { value: "gemini-1.5-flash", label: "gemini-1.5-flash (Legacy fast)" },
      ],
    },
  ];

  let {
    isOpen = false,
    workspaces = [],
    onClose,
    onSaveWorkspace,
    onDeleteWorkspace,
  }: {
    isOpen: boolean;
    workspaces: Workspace[];
    onClose: () => void;
    onSaveWorkspace: (ws: Workspace) => void;
    onDeleteWorkspace: (id: string) => void;
  } = $props();

  let editingWorkspace: Workspace = $state({
    id: "",
    name: "",
    path: "C:\\",
    model: "auto",
    system_prompt: "",
    created_at: new Date().toISOString(),
  });

  let selectedDropdownValue = $state("auto");
  let customModelInput = $state("");

  $effect(() => {
    const isKnown = MODEL_GROUPS.some((g) =>
      g.options.some((o) => o.value === editingWorkspace.model)
    );
    if (isKnown) {
      selectedDropdownValue = editingWorkspace.model;
      customModelInput = "";
    } else {
      selectedDropdownValue = "manual_custom";
      customModelInput = editingWorkspace.model || "";
    }
  });

  function onSelectModelChange(e: Event) {
    const target = e.target as HTMLSelectElement;
    selectedDropdownValue = target.value;
    if (target.value === "manual_custom") {
      editingWorkspace.model = customModelInput.trim() || "gemini-3-pro-preview";
    } else {
      editingWorkspace.model = target.value;
    }
  }

  function onCustomModelInput(e: Event) {
    const target = e.target as HTMLInputElement;
    customModelInput = target.value;
    editingWorkspace.model = target.value;
  }

  function startNew() {
    editingWorkspace = {
      id: "ws-" + Math.random().toString(36).substring(2, 9),
      name: "New Workspace",
      path: "C:\\",
      model: "auto",
      system_prompt: "",
      created_at: new Date().toISOString(),
    };
  }

  function handleSave() {
    if (!editingWorkspace.name.trim()) return;
    onSaveWorkspace({ ...editingWorkspace });
  }
</script>

{#if isOpen}
  <div
    class="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
    onclick={onClose}
    role="presentation"
  >
    <div
      class="w-full max-w-2xl bg-slate-900 border border-slate-700 rounded-2xl shadow-2xl overflow-hidden flex flex-col max-h-[85vh]"
      onclick={(e) => e.stopPropagation()}
      role="dialog"
      aria-modal="true"
    >
      <!-- Header -->
      <div class="px-5 py-4 border-b border-slate-800 flex items-center justify-between">
        <div>
          <h2 class="text-sm font-semibold text-slate-100">Workspace Profiles</h2>
          <p class="text-xs text-slate-400">Configure directory contexts, models, and custom prompts.</p>
        </div>
        <button onclick={onClose} class="p-1 text-slate-400 hover:text-white rounded" aria-label="Close modal">
          <X size={16} />
        </button>
      </div>

      <!-- Content -->
      <div class="flex-1 flex overflow-hidden">
        <!-- Workspaces List -->
        <div class="w-1/3 border-r border-slate-800 p-3 space-y-1.5 overflow-y-auto">
          <button
            onclick={startNew}
            class="w-full flex items-center gap-1.5 px-3 py-2 rounded-lg bg-sky-500/10 hover:bg-sky-500/20 text-sky-400 text-xs font-medium transition-colors mb-2"
          >
            <Plus size={14} />
            <span>Add Workspace</span>
          </button>

          {#each workspaces as ws}
            <button
              onclick={() => (editingWorkspace = { ...ws })}
              class="w-full text-left px-3 py-2 rounded-lg text-xs transition-colors {editingWorkspace.id === ws.id ? 'bg-slate-800 text-sky-400 font-medium' : 'text-slate-300 hover:bg-slate-800/60'}"
            >
              <div class="truncate">{ws.name}</div>
              <div class="text-[10px] text-slate-500 font-mono truncate">{ws.model}</div>
            </button>
          {/each}
        </div>

        <!-- Form Details -->
        <div class="flex-1 p-5 overflow-y-auto space-y-4 text-xs">
          <div>
            <label for="ws-name" class="block text-slate-300 font-medium mb-1">Profile Name</label>
            <input
              id="ws-name"
              type="text"
              bind:value={editingWorkspace.name}
              class="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-lg text-slate-100 focus:outline-none focus:border-sky-500"
            />
          </div>

          <div>
            <label for="ws-path" class="block text-slate-300 font-medium mb-1">Working Directory (Absolute Path)</label>
            <input
              id="ws-path"
              type="text"
              bind:value={editingWorkspace.path}
              class="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-lg text-slate-100 font-mono focus:outline-none focus:border-sky-500"
            />
            <span class="text-[11px] text-slate-500 mt-1 block">Gemini CLI will run inside this directory and auto-read GEMINI.md.</span>
          </div>

          <div>
            <label for="ws-model" class="block text-slate-300 font-medium mb-1">Preferred Model</label>
            <select
              id="ws-model"
              value={selectedDropdownValue}
              onchange={onSelectModelChange}
              class="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-lg text-slate-100 focus:outline-none focus:border-sky-500 font-mono"
            >
              {#each MODEL_GROUPS as group}
                <optgroup label={group.group}>
                  {#each group.options as opt}
                    <option value={opt.value}>{opt.label}</option>
                  {/each}
                </optgroup>
              {/each}
              <optgroup label="Manual / Custom Model">
                <option value="manual_custom">Manual (Specify any available model...)</option>
              </optgroup>
            </select>

            {#if selectedDropdownValue === "manual_custom"}
              <div class="mt-2.5 p-2.5 bg-slate-950/80 border border-sky-500/40 rounded-lg">
                <label for="ws-custom-model" class="block text-[11px] font-medium text-sky-400 mb-1">
                  Custom Model Name / Identifier:
                </label>
                <input
                  id="ws-custom-model"
                  type="text"
                  value={customModelInput}
                  oninput={onCustomModelInput}
                  placeholder="e.g. gemini-3-pro-preview or custom model"
                  class="w-full px-2.5 py-1.5 bg-slate-900 border border-slate-700 rounded text-slate-100 font-mono text-xs focus:outline-none focus:border-sky-400"
                />
                <span class="text-[10px] text-slate-400 mt-1 block">
                  Passed directly to Gemini CLI via ACP protocol.
                </span>
              </div>
            {/if}
            <span class="text-[11px] text-slate-500 mt-1.5 block">
              Auto dynamically balances speed and complexity. Manual lets you select or enter any model.
            </span>
          </div>

          <div>
            <label for="ws-prompt" class="block text-slate-300 font-medium mb-1">Custom System Instructions (Optional)</label>
            <textarea
              id="ws-prompt"
              bind:value={editingWorkspace.system_prompt}
              rows="3"
              placeholder="e.g. Always write production C# code following Microsoft coding conventions..."
              class="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-lg text-slate-100 focus:outline-none focus:border-sky-500 resize-none"
            ></textarea>
          </div>

          <div class="pt-2 flex items-center justify-between border-t border-slate-800">
            <button
              onclick={() => {
                if (confirm("Delete this workspace profile?")) {
                  onDeleteWorkspace(editingWorkspace.id);
                }
              }}
              class="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-rose-400 hover:bg-rose-500/10 transition-colors"
            >
              <Trash2 size={14} />
              <span>Delete</span>
            </button>

            <button
              onclick={handleSave}
              class="flex items-center gap-1.5 px-4 py-1.5 rounded-lg bg-sky-500 hover:bg-sky-400 text-slate-950 font-semibold transition-colors"
            >
              <Check size={14} />
              <span>Save Workspace</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
{/if}
