import type { PlanTaskItem } from "$lib/types";

/**
 * Parses markdown text to extract implementation plan tasks (checkboxes or numbered steps).
 */
export function extractTasksFromMarkdown(md: string): PlanTaskItem[] {
  if (!md || !md.trim()) return [];

  const lines = md.split("\n");
  const extracted: PlanTaskItem[] = [];
  let currentCategory = "Implementation Steps";

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();

    // Heading category
    if (line.startsWith("### ") || line.startsWith("## ")) {
      currentCategory = line.replace(/^#+\s*/, "").replace(/[#*`_]/g, "").trim();
      continue;
    }

    // Checkbox tasks (- [ ] or - [x] or * [ ])
    const checkMatch = line.match(/^[-*]\s*\[([ xX])\]\s*(.*)$/);
    if (checkMatch && checkMatch[2].trim().length > 2) {
      extracted.push({
        id: `task-${Date.now()}-${extracted.length}`,
        text: checkMatch[2].trim(),
        completed: checkMatch[1].toLowerCase() === "x",
        category: currentCategory,
      });
      continue;
    }

    // Step items (e.g. "Step 1: ...")
    const numMatch = line.match(/^(?:(?:\bStep\s+\d+:?))\s*(.*)$/i);
    if (numMatch && numMatch[1].length > 5 && !line.includes("```")) {
      extracted.push({
        id: `task-${Date.now()}-${extracted.length}`,
        text: numMatch[1].trim(),
        completed: false,
        category: currentCategory,
      });
    }
  }

  return extracted;
}

/**
 * Synchronizes completion states of existing tasks based on markdown text.
 * Checks for `- [x]` or `* [x]` items in the text and marks matching tasks as completed.
 */
export function syncTasksCompletionFromMarkdown(tasks: PlanTaskItem[], md: string): PlanTaskItem[] {
  if (!md || tasks.length === 0) return tasks;

  const lines = md.split("\n");
  const completedTexts: string[] = [];

  for (const line of lines) {
    const trimmed = line.trim();
    const match = trimmed.match(/^[-*]\s*\[[xX]\]\s*(.*)$/);
    if (match && match[1].trim()) {
      completedTexts.push(match[1].trim().toLowerCase());
    }
  }

  if (completedTexts.length === 0) return tasks;

  let changed = false;
  const updated = tasks.map((t) => {
    if (t.completed) return t;
    const taskTextLower = t.text.toLowerCase();
    const isCompleted = completedTexts.some(
      (ct) => ct === taskTextLower || ct.includes(taskTextLower) || taskTextLower.includes(ct)
    );
    if (isCompleted) {
      changed = true;
      return { ...t, completed: true };
    }
    return t;
  });

  return changed ? updated : tasks;
}
