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
    WorkspaceFileEntry,
  } from "$lib/types";
  import Sidebar from "$lib/components/Sidebar.svelte";
  import ChatView from "$lib/components/ChatView.svelte";
  import SolutionExplorer from "$lib/components/SolutionExplorer.svelte";
  import SearchModal from "$lib/components/SearchModal.svelte";
  import WorkspaceModal from "$lib/components/WorkspaceModal.svelte";
  import TemplatesModal from "$lib/components/TemplatesModal.svelte";
  import ThemeModal from "$lib/components/ThemeModal.svelte";
  import McpModal from "$lib/components/McpModal.svelte";
  import { themeManager } from "$lib/theme.svelte";
  import { dialogManager } from "$lib/dialog.svelte";

  // Reactive State (Svelte 5 Runes)
  let workspaces: Workspace[] = $state([]);
  let activeWorkspace: Workspace | null = $state(null);
  let sessions: Session[] = $state([]);
  let activeSession: Session | null = $state(null);
  let messages: Message[] = $state([]);
  let promptTemplates: PromptTemplate[] = $state([]);
  let workspaceFiles: WorkspaceFileEntry[] = $state([]);
  // Per-session streaming and permission isolation (prevents cross-session leakage)
  let sessionStreams: Record<string, { text: string; isStreaming: boolean }> = $state({});
  let sessionToolPermissions: Record<string, ToolPermissionPayload> = $state({});
  let activeStreamingSessionId: string | null = $state(null);
  let envStatus: GeminiEnvStatus | null = $state(null);

  // Derived reactive properties for currently viewed session
  let isCurrentSessionStreaming = $derived(
    activeSession ? (sessionStreams[activeSession.id]?.isStreaming ?? false) : false
  );
  let currentStreamingText = $derived(
    activeSession ? (sessionStreams[activeSession.id]?.text ?? "") : ""
  );
  let currentToolPermission = $derived(
    activeSession ? (sessionToolPermissions[activeSession.id] ?? null) : null
  );

  // Modals & Panels
  let showSearchModal = $state(false);
  let showWorkspaceModal = $state(false);
  let showTemplatesModal = $state(false);
  let showThemeModal = $state(false);
  let showMcpModal = $state(false);
  let showTerminalDrawer = $state(false);
  let showSolutionExplorer = $state(true);
  let showSidebar = $state(true);

  let chatViewRef = $state<ReturnType<typeof ChatView> | null>(null);
  let solutionExplorerRef = $state<ReturnType<typeof SolutionExplorer> | null>(null);

  let unlistenChunk: UnlistenFn | null = null;
  let unlistenTool: UnlistenFn | null = null;
  let unlistenError: UnlistenFn | null = null;

  onMount(async () => {
    // 0. Initialize theme
    themeManager.init();

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
        await loadWorkspaceFiles(activeWorkspace.id);
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
      async (event) => {
        const { session_id, delta, is_done } = event.payload;

        // Resolve which session this chunk belongs to:
        let targetId = session_id;
        if (!targetId || !sessionStreams[targetId]) {
          if (activeStreamingSessionId && sessionStreams[activeStreamingSessionId]) {
            targetId = activeStreamingSessionId;
          } else {
            const activeStreaming = Object.keys(sessionStreams).filter(
              (id) => sessionStreams[id]?.isStreaming
            );
            if (activeStreaming.length === 1) {
              targetId = activeStreaming[0];
            } else if (activeSession && sessionStreams[activeSession.id]) {
              targetId = activeSession.id;
            }
          }
        }

        if (!targetId) return;

        if (!sessionStreams[targetId]) {
          sessionStreams[targetId] = { text: "", isStreaming: true };
        }

        sessionStreams[targetId].text += delta;

        if (is_done) {
          await finishSessionStreaming(targetId);
        }
      }
    );

    unlistenTool = await listen<ToolPermissionPayload>(
      "acp-tool-permission",
      (event) => {
        const payload = event.payload;
        let sid = payload.session_id;
        if (!sid || sid === "default") {
          sid = activeStreamingSessionId || activeSession?.id || "default";
        }
        sessionToolPermissions[sid] = payload;
      }
    );

    unlistenError = await listen<any>("acp-error", async (event) => {
      const payload = event.payload;
      const payloadStr = JSON.stringify(payload || "");
      if (payloadStr.toLowerCase().includes("cancel")) {
        console.warn("Ignored cancellation signal:", payload);
        return;
      }
      console.error("ACP Error:", payload);

      let targetId = payload?.sessionId || payload?.session_id;
      if (!targetId || !sessionStreams[targetId]) {
        targetId = activeStreamingSessionId || (activeSession?.id ?? "");
      }

      if (targetId && sessionStreams[targetId]) {
        sessionStreams[targetId].text += `\n\n**Error:** ${payloadStr}`;
        await finishSessionStreaming(targetId);
      }
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
    } else if ((e.ctrlKey || e.metaKey) && e.key === "m") {
      e.preventDefault();
      showMcpModal = !showMcpModal;
    } else if ((e.ctrlKey || e.metaKey) && (e.key === "`" || e.key === "~")) {
      e.preventDefault();
      showTerminalDrawer = !showTerminalDrawer;
    } else if ((e.ctrlKey || e.metaKey) && (e.key === "b" || e.key === "B")) {
      e.preventDefault();
      showSidebar = !showSidebar;
    } else if ((e.ctrlKey || e.metaKey) && e.altKey && (e.key === "l" || e.key === "L")) {
      e.preventDefault();
      showSolutionExplorer = !showSolutionExplorer;
    } else if ((e.ctrlKey || e.metaKey) && e.key === ";") {
      e.preventDefault();
      if (!showSolutionExplorer) showSolutionExplorer = true;
      setTimeout(() => {
        solutionExplorerRef?.focusSearch();
      }, 50);
    }
  }

  async function loadWorkspaceFiles(wsId: string) {
    try {
      workspaceFiles = await invoke<WorkspaceFileEntry[]>("list_workspace_files", { workspaceId: wsId });
    } catch (e) {
      console.warn("Failed to list workspace files:", e);
      workspaceFiles = [];
    }
  }

  async function refreshWorkspaceFiles() {
    if (activeWorkspace) {
      await loadWorkspaceFiles(activeWorkspace.id);
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
    await loadWorkspaceFiles(ws.id);
  }

  async function selectSession(session: Session) {
    if (activeSession?.id === session.id) return;
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
    const newTitle = await dialogManager.prompt("Enter new title for conversation:", session.title, {
      title: "Rename Conversation",
      placeholder: "New conversation title...",
    });
    if (!newTitle || !newTitle.trim() || newTitle === session.title) return;
    const trimmed = newTitle.trim();
    await invoke("rename_session", { sessionId: session.id, title: trimmed });
    session.title = trimmed;
    sessions = [...sessions];
  }

  async function handleDeleteSession(session: Session) {
    const confirmed = await dialogManager.confirm(`Delete conversation "${session.title}"? This cannot be undone.`, {
      title: "Delete Conversation",
      confirmText: "Delete",
      isDestructive: true,
    });
    if (!confirmed) return;

    // Clean up any streaming and permission state for deleted session
    if (sessionStreams[session.id]) {
      try {
        await invoke("cancel_prompt", { requestId: 1, sessionId: session.id });
      } catch (e) {
        // ignore
      }
      delete sessionStreams[session.id];
    }
    if (sessionToolPermissions[session.id]) {
      delete sessionToolPermissions[session.id];
    }
    if (activeStreamingSessionId === session.id) {
      activeStreamingSessionId = null;
    }

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

    const targetSessionId = activeSession.id;

    // Check if another session is already actively streaming
    const busySessionId = Object.keys(sessionStreams).find(
      (id) => id !== targetSessionId && sessionStreams[id]?.isStreaming
    );
    if (busySessionId) {
      const busySession = sessions.find((s) => s.id === busySessionId);
      const busyTitle = busySession?.title || "another conversation";
      await dialogManager.alert(
        `Gemini CLI is currently generating a response in "${busyTitle}". Please wait for it to finish or stop it before sending a new prompt.`,
        "Gemini Generating"
      );
      return;
    }

    // Auto-update title if it's default
    if (activeSession.title === "New Conversation") {
      const summary = prompt.substring(0, 32) + (prompt.length > 32 ? "..." : "");
      await invoke("rename_session", { sessionId: targetSessionId, title: summary });
      activeSession.title = summary;
      sessions = [...sessions];
    }

    // Add user message to UI immediately if active
    const userMsg: Message = {
      id: "temp-" + Date.now(),
      session_id: targetSessionId,
      role: "user",
      content: prompt,
      token_count: 0,
      created_at: new Date().toISOString(),
    };
    if (activeSession?.id === targetSessionId) {
      messages = [...messages, userMsg];
    }

    // Initialize session stream state
    activeStreamingSessionId = targetSessionId;
    sessionStreams[targetSessionId] = {
      text: "",
      isStreaming: true,
    };

    try {
      await invoke("send_prompt", {
        sessionId: targetSessionId,
        workspaceId: activeWorkspace.id,
        prompt,
        model: activeWorkspace.model,
      });
    } catch (err: any) {
      const errMsg = `**Error starting prompt:** ${err?.toString() || err}`;
      if (sessionStreams[targetSessionId]) {
        sessionStreams[targetSessionId].text = errMsg;
      }
      await finishSessionStreaming(targetSessionId);
    }
  }

  async function finishSessionStreaming(targetSessionId: string) {
    const stream = sessionStreams[targetSessionId];
    const textToSave = stream?.text || "";

    if (activeStreamingSessionId === targetSessionId) {
      activeStreamingSessionId = null;
    }

    if (sessionStreams[targetSessionId]) {
      sessionStreams[targetSessionId].isStreaming = false;
    }

    if (textToSave) {
      const assistantMsg: Message = {
        id: "msg-" + Date.now(),
        session_id: targetSessionId, // STRICTLY saved to initiating session!
        role: "assistant",
        content: textToSave,
        token_count: Math.ceil(textToSave.length / 4),
        created_at: new Date().toISOString(),
      };

      try {
        await invoke("save_message", { msg: assistantMsg });
      } catch (e) {
        console.error("Failed to save assistant message:", e);
      }

      // Only update on-screen `messages` if the user is currently viewing this exact session
      if (activeSession?.id === targetSessionId) {
        messages = [...messages, assistantMsg];
      }
    }

    delete sessionStreams[targetSessionId];
  }

  async function handleCancelSessionPrompt(targetSessionId: string) {
    try {
      await invoke("cancel_prompt", { requestId: 1, sessionId: targetSessionId });
    } catch (e) {
      console.warn("Cancel signal error:", e);
    }
    await finishSessionStreaming(targetSessionId);
  }

  async function handleCancelPrompt() {
    if (!activeSession) return;
    await handleCancelSessionPrompt(activeSession.id);
  }

  async function handleToolResponse(requestId: number, optionId?: string, allowed: boolean = true) {
    if (activeSession && sessionToolPermissions[activeSession.id]) {
      delete sessionToolPermissions[activeSession.id];
    }
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
      await dialogManager.alert("Failed to export chat: " + e, "Export Failed");
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
      await loadWorkspaceFiles(ws.id);
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

<div class="flex h-screen w-screen bg-app overflow-hidden select-none">
  <!-- Left Navigation Sidebar -->
  {#if showSidebar}
    <Sidebar
      {workspaces}
      {activeWorkspace}
      {sessions}
      {activeSession}
      {sessionStreams}
      {sessionToolPermissions}
      {envStatus}
      onToggle={() => (showSidebar = false)}
      onSelectWorkspace={selectWorkspace}
      onSelectSession={selectSession}
      onNewSession={handleNewSession}
      onRenameSession={handleRenameSession}
      onDeleteSession={handleDeleteSession}
      onCancelSession={handleCancelSessionPrompt}
      onOpenSearch={() => (showSearchModal = true)}
      onOpenTemplates={() => (showTemplatesModal = true)}
      onOpenWorkspaceModal={() => (showWorkspaceModal = true)}
      onOpenThemeModal={() => (showThemeModal = true)}
      onOpenMcpModal={() => (showMcpModal = true)}
      onToggleTerminal={() => (showTerminalDrawer = !showTerminalDrawer)}
    />
  {/if}

  <!-- Main Chat Surface -->
  <ChatView
    bind:this={chatViewRef}
    workspace={activeWorkspace}
    session={activeSession}
    {workspaceFiles}
    {messages}
    isStreaming={isCurrentSessionStreaming}
    streamingText={currentStreamingText}
    toolPermission={currentToolPermission}
    {showSidebar}
    onToggleSidebar={() => (showSidebar = !showSidebar)}
    bind:showTerminalDrawer
    bind:showSolutionExplorer
    onSendPrompt={handleSendPrompt}
    onCancelPrompt={handleCancelPrompt}
    onToolResponse={handleToolResponse}
    onExport={handleExport}
    onOpenMcpModal={() => (showMcpModal = true)}
  />

  <!-- Right Visual Studio 2022 Workspace Explorer -->
  <SolutionExplorer
    bind:this={solutionExplorerRef}
    workspace={activeWorkspace}
    {workspaceFiles}
    isOpen={showSolutionExplorer}
    onToggle={() => (showSolutionExplorer = !showSolutionExplorer)}
    onRefresh={refreshWorkspaceFiles}
    onInsertMention={(relPath) => {
      chatViewRef?.insertFileMention(relPath);
    }}
    onAttachItem={(relPath, isDir, name) => {
      chatViewRef?.attachItem(relPath, isDir, name);
    }}
    onAttachMultiple={(items) => {
      chatViewRef?.attachMultiple(items);
    }}
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

  <ThemeModal
    isOpen={showThemeModal}
    onClose={() => (showThemeModal = false)}
  />

  <McpModal
    isOpen={showMcpModal}
    {activeWorkspace}
    onClose={() => (showMcpModal = false)}
  />
</div>
