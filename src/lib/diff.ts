export interface DiffLine {
  type: "add" | "del" | "same" | "header";
  oldLineNumber?: number;
  newLineNumber?: number;
  content: string;
}

export interface DiffHunk {
  header?: string;
  lines: DiffLine[];
}

export interface DiffResult {
  fileName?: string;
  additions: number;
  deletions: number;
  hunks: DiffHunk[];
  isTruncated?: boolean;
}

/**
 * Computes line-by-line diff between two strings using Longest Common Subsequence (LCS).
 * Produces clean hunk segments with line numbering and statistics.
 */
export function computeLineDiff(
  oldText: string = "",
  newText: string = "",
  fileName?: string,
  contextLines: number = 3
): DiffResult {
  const oldLines = oldText ? oldText.split(/\r?\n/) : [];
  const newLines = newText ? newText.split(/\r?\n/) : [];

  // Edge cases: empty oldText or empty newText
  if (oldLines.length === 0 && newLines.length === 0) {
    return { fileName, additions: 0, deletions: 0, hunks: [] };
  }

  if (oldLines.length === 0) {
    const lines: DiffLine[] = newLines.map((content, idx) => ({
      type: "add",
      newLineNumber: idx + 1,
      content,
    }));
    return {
      fileName,
      additions: newLines.length,
      deletions: 0,
      hunks: [{ header: `@@ +1,${newLines.length} @@`, lines }],
    };
  }

  if (newLines.length === 0) {
    const lines: DiffLine[] = oldLines.map((content, idx) => ({
      type: "del",
      oldLineNumber: idx + 1,
      content,
    }));
    return {
      fileName,
      additions: 0,
      deletions: oldLines.length,
      hunks: [{ header: `@@ -1,${oldLines.length} @@`, lines }],
    };
  }

  // Cap line comparison for performance if lines exceed 1200
  const maxLines = 1200;
  const isTruncated = oldLines.length > maxLines || newLines.length > maxLines;
  const oldSlice = isTruncated ? oldLines.slice(0, maxLines) : oldLines;
  const newSlice = isTruncated ? newLines.slice(0, maxLines) : newLines;

  const m = oldSlice.length;
  const n = newSlice.length;

  // Optimize: LCS table using Uint16Array for low memory overhead
  const dp: number[][] = Array.from({ length: m + 1 }, () => new Array(n + 1).fill(0));

  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      if (oldSlice[i - 1] === newSlice[j - 1]) {
        dp[i][j] = dp[i - 1][j - 1] + 1;
      } else {
        dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
      }
    }
  }

  // Backtrack to recover the diff sequence
  const rawDiff: { type: "add" | "del" | "same"; oldIdx?: number; newIdx?: number; content: string }[] = [];
  let i = m;
  let j = n;

  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && oldSlice[i - 1] === newSlice[j - 1]) {
      rawDiff.push({
        type: "same",
        oldIdx: i,
        newIdx: j,
        content: oldSlice[i - 1],
      });
      i--;
      j--;
    } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
      rawDiff.push({
        type: "add",
        newIdx: j,
        content: newSlice[j - 1],
      });
      j--;
    } else if (i > 0 && (j === 0 || dp[i][j - 1] < dp[i - 1][j])) {
      rawDiff.push({
        type: "del",
        oldIdx: i,
        content: oldSlice[i - 1],
      });
      i--;
    }
  }

  rawDiff.reverse();

  let additions = 0;
  let deletions = 0;
  const diffLines: DiffLine[] = [];

  for (const item of rawDiff) {
    if (item.type === "add") additions++;
    if (item.type === "del") deletions++;

    diffLines.push({
      type: item.type,
      oldLineNumber: item.oldIdx,
      newLineNumber: item.newIdx,
      content: item.content,
    });
  }

  // If no changes, return 1 hunk with lines
  if (additions === 0 && deletions === 0) {
    return {
      fileName,
      additions: 0,
      deletions: 0,
      hunks: [
        {
          header: `@@ 1,${diffLines.length} (no changes) @@`,
          lines: diffLines.slice(0, 15),
        },
      ],
      isTruncated,
    };
  }

  // Group into hunks with context lines
  const hunks: DiffHunk[] = [];
  let currentHunkLines: DiffLine[] = [];
  let lastChangeIdx = -Infinity;

  for (let idx = 0; idx < diffLines.length; idx++) {
    const line = diffLines[idx];
    const isChange = line.type === "add" || line.type === "del";

    if (isChange) {
      if (currentHunkLines.length === 0) {
        // Start new hunk, include preceding context lines
        const contextStart = Math.max(0, idx - contextLines);
        for (let c = contextStart; c < idx; c++) {
          currentHunkLines.push(diffLines[c]);
        }
      }
      currentHunkLines.push(line);
      lastChangeIdx = idx;
    } else if (currentHunkLines.length > 0) {
      // Include trailing context lines
      if (idx - lastChangeIdx <= contextLines) {
        currentHunkLines.push(line);
      } else {
        // Hunk finished
        const startOld = currentHunkLines.find((l) => l.oldLineNumber)?.oldLineNumber || 1;
        const startNew = currentHunkLines.find((l) => l.newLineNumber)?.newLineNumber || 1;
        hunks.push({
          header: `@@ -${startOld} +${startNew} @@`,
          lines: currentHunkLines,
        });
        currentHunkLines = [];
      }
    }
  }

  if (currentHunkLines.length > 0) {
    const startOld = currentHunkLines.find((l) => l.oldLineNumber)?.oldLineNumber || 1;
    const startNew = currentHunkLines.find((l) => l.newLineNumber)?.newLineNumber || 1;
    hunks.push({
      header: `@@ -${startOld} +${startNew} @@`,
      lines: currentHunkLines,
    });
  }

  return {
    fileName,
    additions,
    deletions,
    hunks: hunks.length > 0 ? hunks : [{ header: "@@ -1 +1 @@", lines: diffLines }],
    isTruncated,
  };
}

/**
 * Parses unified git patch strings into standard DiffResult data structure.
 */
export function parseUnifiedPatch(patch: string, defaultFileName?: string): DiffResult {
  const lines = patch.split(/\r?\n/);
  const hunks: DiffHunk[] = [];
  let currentHunk: DiffHunk | null = null;
  let additions = 0;
  let deletions = 0;
  let detectedFileName = defaultFileName;

  let oldCounter = 1;
  let newCounter = 1;

  for (const line of lines) {
    if (line.startsWith("--- a/") || line.startsWith("--- ")) {
      const fn = line.replace(/^---\s+(a\/)?/, "").trim();
      if (fn && fn !== "/dev/null") detectedFileName = fn;
      continue;
    }
    if (line.startsWith("+++ b/") || line.startsWith("+++ ")) {
      const fn = line.replace(/^\+\+\+\s+(b\/)?/, "").trim();
      if (fn && fn !== "/dev/null") detectedFileName = fn;
      continue;
    }

    if (line.startsWith("@@")) {
      // Parse header e.g. @@ -10,6 +10,7 @@
      const match = line.match(/@@\s+-(\d+)(?:,\d+)?\s+\+(\d+)(?:,\d+)?\s+@@/);
      if (match) {
        oldCounter = parseInt(match[1], 10);
        newCounter = parseInt(match[2], 10);
      }

      currentHunk = {
        header: line,
        lines: [],
      };
      hunks.push(currentHunk);
      continue;
    }

    if (!currentHunk) {
      currentHunk = {
        header: "@@ diff @@",
        lines: [],
      };
      hunks.push(currentHunk);
    }

    if (line.startsWith("+")) {
      additions++;
      currentHunk.lines.push({
        type: "add",
        newLineNumber: newCounter++,
        content: line.substring(1),
      });
    } else if (line.startsWith("-")) {
      deletions++;
      currentHunk.lines.push({
        type: "del",
        oldLineNumber: oldCounter++,
        content: line.substring(1),
      });
    } else {
      const text = line.startsWith(" ") ? line.substring(1) : line;
      currentHunk.lines.push({
        type: "same",
        oldLineNumber: oldCounter++,
        newLineNumber: newCounter++,
        content: text,
      });
    }
  }

  return {
    fileName: detectedFileName,
    additions,
    deletions,
    hunks,
  };
}

export interface ExtractedDiffPayload {
  isDiffAvailable: boolean;
  oldText?: string;
  newText?: string;
  patch?: string;
  filePath?: string;
  command?: string;
  toolCategory: "edit" | "command" | "read" | "other";
}

/**
 * Extracts diff or code change payloads from various ACP tool execution signatures.
 */
export function extractDiffData(
  parameters: any,
  locations?: any,
  content?: any,
  toolKind?: string,
  toolName?: string
): ExtractedDiffPayload {
  const normKind = (toolKind || "").toLowerCase();
  const normName = (toolName || "").toLowerCase();

  // Extract file path from locations or parameters
  let filePath: string | undefined;
  if (Array.isArray(locations) && locations.length > 0) {
    const first = locations[0];
    filePath = typeof first === "string" ? first : first?.path || first?.uri;
  } else if (typeof locations === "string") {
    filePath = locations;
  } else if (locations && typeof locations === "object") {
    filePath = locations.path || locations.uri;
  }

  if (!filePath && parameters && typeof parameters === "object") {
    filePath =
      parameters.path ||
      parameters.filePath ||
      parameters.file_path ||
      parameters.file ||
      parameters.targetFile ||
      parameters.target_file ||
      parameters.fileName ||
      parameters.file_name;
  }

  // Check command execution
  if (
    normKind === "execute" ||
    normKind === "command" ||
    normName.includes("bash") ||
    normName.includes("terminal") ||
    normName.includes("exec") ||
    normName.includes("run")
  ) {
    const cmd =
      parameters?.command ||
      parameters?.cmd ||
      parameters?.script ||
      (typeof parameters === "string" ? parameters : undefined);
    if (cmd) {
      return {
        isDiffAvailable: false,
        command: cmd,
        toolCategory: "command",
      };
    }
  }

  // Check unified patch in parameters or content
  const possiblePatch =
    parameters?.patch ||
    parameters?.diff ||
    (typeof content === "string" && content.includes("@@") ? content : undefined) ||
    (typeof parameters === "string" && parameters.includes("@@") ? parameters : undefined);

  if (possiblePatch && typeof possiblePatch === "string" && (possiblePatch.includes("@@") || possiblePatch.startsWith("---"))) {
    return {
      isDiffAvailable: true,
      patch: possiblePatch,
      filePath,
      toolCategory: "edit",
    };
  }

  // Check oldText / newText pairs
  const oldText =
    parameters?.oldText ??
    parameters?.old_content ??
    parameters?.oldContent ??
    parameters?.targetContent ??
    parameters?.target_content ??
    parameters?.old_str ??
    parameters?.oldString;

  const newText =
    parameters?.newText ??
    parameters?.new_content ??
    parameters?.newContent ??
    parameters?.replacementContent ??
    parameters?.replacement_content ??
    parameters?.new_str ??
    parameters?.newString;

  if (newText !== undefined && (oldText !== undefined || normKind === "edit" || normName.includes("edit") || normName.includes("replace"))) {
    return {
      isDiffAvailable: true,
      oldText: oldText !== undefined ? String(oldText) : undefined,
      newText: String(newText),
      filePath,
      toolCategory: "edit",
    };
  }

  // Check whole file content write (e.g. write_to_file, create_file)
  const fullContent =
    parameters?.content ??
    parameters?.code ??
    parameters?.file_text ??
    parameters?.codeContent;

  if (fullContent !== undefined && filePath) {
    return {
      isDiffAvailable: true,
      newText: String(fullContent),
      filePath,
      toolCategory: "edit",
    };
  }

  const isEdit =
    normKind === "edit" ||
    normName.includes("edit") ||
    normName.includes("write") ||
    normName.includes("patch") ||
    normName.includes("modify");

  return {
    isDiffAvailable: false,
    filePath,
    toolCategory: isEdit ? "edit" : "other",
  };
}
