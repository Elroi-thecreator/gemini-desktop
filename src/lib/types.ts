export interface Workspace {
  id: string;
  name: string;
  path: string;
  model: string;
  system_prompt?: string;
  created_at: string;
}

export interface Session {
  id: string;
  workspace_id: string;
  title: string;
  created_at: string;
  updated_at: string;
}

export interface Message {
  id: string;
  session_id: string;
  role: "user" | "assistant" | "system" | "tool";
  content: string;
  tool_calls_json?: string;
  token_count: number;
  created_at: string;
}

export interface PromptTemplate {
  id: string;
  title: string;
  category: string;
  content: string;
}

export interface SearchResult {
  message_id: string;
  session_id: string;
  session_title: string;
  role: string;
  snippet: string;
  created_at: string;
}

export interface ToolPermissionPayload {
  request_id: number;
  tool_name: string;
  parameters: any;
  reason?: string;
}

export interface GeminiEnvStatus {
  installed: boolean;
  path?: string;
  version?: string;
  details: string;
}
