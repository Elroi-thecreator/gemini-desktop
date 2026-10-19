import type { Message, ToolExecutionStep } from "$lib/types";

/**
 * Extracts structured tool execution steps from an assistant message.
 * Supports modern structured tool_calls_json as well as legacy inline markdown logs.
 */
export function parseToolSteps(msg: Message): ToolExecutionStep[] {
  if (msg.tool_calls_json) {
    try {
      const parsed = JSON.parse(msg.tool_calls_json);
      if (Array.isArray(parsed) && parsed.length > 0) {
        return parsed;
      }
    } catch (e) {
      console.warn("Failed to parse tool_calls_json:", e);
    }
  }

  // Fallback: parse legacy markdown blockquotes in existing saved conversations
  if (
    msg.content &&
    (msg.content.includes("**Running tool:**") ||
      msg.content.includes("**Completed:**") ||
      msg.content.includes("**Tool failed:**"))
  ) {
    const steps: ToolExecutionStep[] = [];
    const lines = msg.content.split("\n");
    let currentStep: ToolExecutionStep | null = null;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i].trim();
      const runMatch = line.match(/>\s*(?:⚙️|\*)\s*\*\*Running tool:\*\*\s*`?([^`]+)`?/i);
      const doneMatch = line.match(/>\s*(?:✅|\*)\s*\*\*Completed:\*\*\s*`?([^`]+)`?/i);
      const failMatch = line.match(/>\s*(?:❌|\*)\s*\*\*Tool failed:\*\*\s*`?([^`:]+)`?(?::\s*(.*))?/i);

      if (runMatch) {
        const title = runMatch[1].replace(/\.{3}$/, "").trim();
        currentStep = {
          id: `legacy-${steps.length}`,
          title,
          tool_name: title,
          status: "completed", // Legacy messages in DB are already finished
        };
        steps.push(currentStep);
      } else if (doneMatch && !currentStep) {
        const title = doneMatch[1].trim();
        steps.push({
          id: `legacy-${steps.length}`,
          title,
          tool_name: title,
          status: "completed",
        });
      } else if (failMatch) {
        if (currentStep) {
          currentStep.status = "failed";
          if (failMatch[2]) currentStep.error = failMatch[2].trim();
        } else {
          const title = failMatch[1].trim();
          steps.push({
            id: `legacy-${steps.length}`,
            title,
            tool_name: title,
            status: "failed",
            error: failMatch[2]?.trim(),
          });
        }
      }
    }

    if (steps.length > 0) {
      return steps;
    }
  }

  return [];
}

/**
 * Strips legacy tool execution blockquotes from message content if they have been extracted
 * into collapsible tool cards, preserving the final assistant synthesis.
 */
export function cleanMessageContent(content: string, hasExtractedSteps: boolean): string {
  if (!content) return "";
  if (!hasExtractedSteps) return content;

  return content
    .replace(/>\s*⚙️\s*\*\*Running tool:\*\*.*?(?:\r?\n|$)/gi, "")
    .replace(/>\s*✅\s*\*\*Completed:\*\*.*?(?:\r?\n|$)/gi, "")
    .replace(/>\s*❌\s*\*\*Tool failed:\*\*.*?(?:\r?\n|$)/gi, "")
    .trim();
}
