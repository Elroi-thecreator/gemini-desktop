<script lang="ts">
  import { onMount, onDestroy } from "svelte";
  import { invoke } from "@tauri-apps/api/core";
  import { listen, type UnlistenFn } from "@tauri-apps/api/event";
  import type {
    Workspace,
    Session,
    Message,
    PromptTemplate,
    SearchResult,
    ToolPermissionPayload,
    GeminiEnvStatus,
  } from "$lib/types";
  import Sidebar from "$lib/components/Sidebar.svelte";
  import ChatView from "$lib/components/ChatView.svelte";
  import SearchModal from "$lib/components/SearchModal.svelte";
  import WorkspaceModal from "$lib/components/WorkspaceModal.svelte";
  import TemplatesModal from "$lib/components/TemplatesModal.svelte";

  // Reactive State (Svelte 5 Runes)
  let workspaces: Workspace[] = $state([]);
  let activeWorkspace: Workspace | null = $state(null);
  let sessions: Session[] = $state([]);
  let activeSession: Session | null = $state(null);
  let messages: Message[] = $state([]);
  let promptTemplates: PromptTemplate[] = $state([]);

  let isStreaming = $state(false);
  let streamingText = $state("");
  let toolPermission: ToolPermissionPayload | null = $state(null);
  let envStatus: GeminiEnvStatus | null = $state(null);

  // Modals
  let showSearchModal = $state(false);
  let showWorkspaceModal = $state(false);
  let showTemplatesModal = $state(false);

  let unlistenChunk: UnlistenFn | null = null;
  let unlistenTool: UnlistenFn | null = null;
  let unlistenError: UnlistenFn | null = null;

  onMount(async () => {
    // 1. Check Gemini environment status
    try {
      envStatus = await invoke<GeminiEnvStatus>("check_gemini_env");
    } catch (e) {
      console.warn("Could not check gemini environment:", e);
    }

    // 2. Load Workspaces
    try {
      workspaces = await invoke<Workspace[]>("get_workspaces");
      if (workspaces.length > 0) {
        activeWorkspace = workspaces[0];
        await loadSessionsForWorkspace(activeWorkspace.id);
      }
    } catch (e) {
      console.error("Failed to load workspaces:", e);
    }

    // 3. Load Templates
    try {
      promptTemplates = await invoke<PromptTemplate[]>("get_prompts");
    } catch (e) {
      console.error("Failed to load prompt templates:", e);
    }

    // 4. Listen to Tauri backend ACP events
    unlistenChunk = await listen<{ session_id: string; delta: string; is_done: boolean }>(
      "acp-chunk",
      (event) => {
        const { delta, is_done } = event.payload;
        streamingText += delta;

        if (is_done) {
          finishStreaming();
        }
      }
    );

    unlistenTool = await listen<ToolPermissionPayload>(
      "acp-tool-permission",
      (event) => {
        toolPermission = event.payload;
      }
    );

    unlistenError = await listen<any>("acp-error", (event) => {
      console.error("ACP Error:", event.payload);
      streamingText += `\n\n**Error:** ${JSON.stringify(event.payload)}`;
      finishStreaming();
    });

    window.addEventListener("keydown", handleGlobalShortcuts);
  });

  onDestroy(() => {
    if (unlistenChunk) unlistenChunk();
    if (unlistenTool) unlistenTool();
    if (unlistenError) unlistenError();
    window.removeEventListener("keydown", handleGlobalShortcuts);
  });

  function handleGlobalShortcuts(e: KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key === "n") {
      e.preventDefault();
      handleNewSession();
    } else if ((e.ctrlKey || e.metaKey) && e.key === "k") {
      e.preventDefault();
      showSearchModal = true;
    }
  }

  async function loadSessionsForWorkspace(wsId: string) {
    sessions = await invoke<Session[]>("get_sessions", { workspaceId: wsId });
    if (sessions.length > 0) {
      await selectSession(sessions[0]);
    } else {
      activeSession = null;
      messages = [];
    }
  }

  async function selectWorkspace(ws: Workspace) {
    activeWorkspace = ws;
    await loadSessionsForWorkspace(ws.id);
  }

  async function selectSession(session: Session) {
    activeSession = session;
    messages = await invoke<Message[]>("get_session_messages", { sessionId: session.id });
  }

  async function handleNewSession() {
    if (!activeWorkspace) return;
    const newSession = await invoke<Session>("create_session", {
      workspaceId: activeWorkspace.id,
      title: "New Conversation",
    });
    sessions = [newSession, ...sessions];
    await selectSession(newSession);
  }

  async function handleRenameSession(session: Session) {
    const newTitle = prompt("Enter new title for conversation:", session.title);
    if (!newTitle || newTitle === session.title) return;
    await invoke("rename_session", { sessionId: session.id, title: newTitle });
    session.title = newTitle;
    sessions = [...sessions];
  }

  async function handleDeleteSession(session: Session) {
    if (!confirm(`Delete conversation "${session.title}"?`)) return;
    await invoke("delete_session", { sessionId: session.id });
    sessions = sessions.filter((s) => s.id !== session.id);
    if (activeSession?.id === session.id) {
      if (sessions.length > 0) {
        await selectSession(sessions[0]);
      } else {
        activeSession = null;
        messages = [];
      }
    }
  }

  async function handleSendPrompt(prompt: string) {
    if (!activeWorkspace) return;

    if (!activeSession) {
      await handleNewSession();
    }
    if (!activeSession) return;

    // Auto-update title if it's default
    if (activeSession.title === "New Conversation") {
      const summary = prompt.substring(0, 32) + (prompt.length > 32 ? "..." : "");
      await invoke("rename_session", { sessionId: activeSession.id, title: summary });
      activeSession.title = summary;
      sessions = [...sessions];
    }

    // Add user message to UI immediately
    const userMsg: Message = {
      id: "temp-" + Date.now(),
      session_id: activeSession.id,
      role: "user",
      content: prompt,
      token_count: 0,
      created_at: new Date().toISOString(),
    };
    messages = [...messages, userMsg];

    isStreaming = true;
    streamingText = "";

    try {
      await invoke("send_prompt", {
        sessionId: activeSession.id,
        workspaceId: activeWorkspace.id,
        prompt,
        model: activeWorkspace.model,
      });
    } catch (err: any) {
      streamingText = `**Error starting prompt:** ${err?.toString() || err}`;
      finishStreaming();
    }
  }

  async function finishStreaming() {
    isStreaming = false;
    if (activeSession && streamingText) {
      const assistantMsg: Message = {
        id: "msg-" + Date.now(),
        session_id: activeSession.id,
        role: "assistant",
        content: streamingText,
        token_count: Math.ceil(streamingText.length / 4),
        created_at: new Date().toISOString(),
      };
      await invoke("save_message", { msg: assistantMsg });
      messages = [...messages, assistantMsg];
      streamingText = "";
    }
  }

  async function handleCancelPrompt() {
    isStreaming = false;
    try {
      await invoke("cancel_prompt", { requestId: 1, sessionId: activeSession?.id });
    } catch (e) {
      console.warn("Cancel signal error:", e);
    }
    finishStreaming();
  }

  async function handleToolResponse(requestId: number, optionId?: string, allowed: boolean = true) {
    toolPermission = null;
    try {
      await invoke("respond_tool_permission", { requestId, optionId, allowed });
    } catch (e) {
      console.error("Failed to send tool response:", e);
    }
  }

  async function handleExport(format: string) {
    if (!activeSession) return;
    try {
      const exported = await invoke<string>("export_session", {
        sessionId: activeSession.id,
        format,
      });

      // Download file to user's computer
      const blob = new Blob([exported], { type: "text/plain;charset=utf-8" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${activeSession.title.replace(/[^a-zA-Z0-9]/g, "_")}.${format}`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      alert("Failed to export chat: " + e);
    }
  }

  async function handleSearch(query: string): Promise<SearchResult[]> {
    return await invoke<SearchResult[]>("search_history", { query });
  }

  async function handleSaveWorkspace(ws: Workspace) {
    await invoke("save_workspace", { workspace: ws });
    workspaces = await invoke<Workspace[]>("get_workspaces");
    if (activeWorkspace?.id === ws.id) {
      activeWorkspace = ws;
    }
    showWorkspaceModal = false;
  }

  async function handleDeleteWorkspace(id: string) {
    await invoke("delete_workspace", { id });
    workspaces = await invoke<Workspace[]>("get_workspaces");
    if (activeWorkspace?.id === id && workspaces.length > 0) {
      await selectWorkspace(workspaces[0]);
    }
    showWorkspaceModal = false;
  }
</script>

<div class="flex h-screen w-screen bg-slate-950 overflow-hidden select-none">
  <!-- Left Navigation Sidebar -->
  <Sidebar
    {workspaces}
    {activeWorkspace}
    {sessions}
    {activeSession}
    {envStatus}
    onSelectWorkspace={selectWorkspace}
    onSelectSession={selectSession}
    onNewSession={handleNewSession}
    onRenameSession={handleRenameSession}
    onDeleteSession={handleDeleteSession}
    onOpenSearch={() => (showSearchModal = true)}
    onOpenTemplates={() => (showTemplatesModal = true)}
    onOpenWorkspaceModal={() => (showWorkspaceModal = true)}
  />

  <!-- Main Chat Surface -->
  <ChatView
    workspace={activeWorkspace}
    session={activeSession}
    {messages}
    {isStreaming}
    {streamingText}
    {toolPermission}
    onSendPrompt={handleSendPrompt}
    onCancelPrompt={handleCancelPrompt}
    onToolResponse={handleToolResponse}
    onExport={handleExport}
  />

  <!-- Modals -->
  <SearchModal
    isOpen={showSearchModal}
    onClose={() => (showSearchModal = false)}
    onSearch={handleSearch}
    onSelectResult={async (res) => {
      const foundSession = sessions.find((s) => s.id === res.session_id);
      if (foundSession) {
        await selectSession(foundSession);
      }
    }}
  />

  <WorkspaceModal
    isOpen={showWorkspaceModal}
    {workspaces}
    onClose={() => (showWorkspaceModal = false)}
    onSaveWorkspace={handleSaveWorkspace}
    onDeleteWorkspace={handleDeleteWorkspace}
  />

  <TemplatesModal
    isOpen={showTemplatesModal}
    templates={promptTemplates}
    onClose={() => (showTemplatesModal = false)}
    onSelectTemplate={(prompt) => {
      handleSendPrompt(prompt);
    }}
  />
</div>
