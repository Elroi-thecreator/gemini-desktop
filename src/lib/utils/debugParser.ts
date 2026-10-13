import type { ParsedDebugError, FileSnippetResult } from "$lib/types";

/**
 * Extracts the file name from a Windows or Unix file path
 */
export function extractFileName(filePath: string): string {
  if (!filePath) return "UnknownFile";
  const normalized = filePath.replace(/\\/g, "/");
  const parts = normalized.split("/");
  return parts[parts.length - 1] || filePath;
}

/**
 * Determines whether raw text corresponds to a runtime error or debugger exception
 */
export function parseDebugError(rawText: string): ParsedDebugError | null {
  if (!rawText || typeof rawText !== "string") return null;
  const trimmed = rawText.trim();
  if (trimmed.length < 15) return null;

  // 1. Visual Studio Exception Helper JSON (from "Copy details" button or Ctrl+C on exception popup)
  if (trimmed.startsWith("{") && (trimmed.includes("StackTraceString") || trimmed.includes("ClassName") || trimmed.includes("InnerException"))) {
    try {
      const data = JSON.parse(trimmed);
      const exType = data.ClassName || "Runtime Exception";
      const exMsg = data.Message || "";
      const trace = data.StackTraceString || trimmed;

      // Extract file and line from StackTraceString (supports EN "line" and DE/FR variants)
      const stackMatch = trace.match(/in\s+([A-Za-z]:\\[^:\r\n]+|\/[^:\r\n]+):(line|zeile|ligne)\s+(\d+)/i) ||
                         trace.match(/in\s+(.+?):(line|zeile|ligne)\s+(\d+)/i);

      let filePath = stackMatch ? stackMatch[1].trim() : undefined;
      let lineNum = stackMatch ? parseInt(stackMatch[3], 10) : undefined;

      return {
        exceptionType: exType,
        message: exMsg,
        filePath,
        fileName: filePath ? extractFileName(filePath) : undefined,
        lineNumber: lineNum,
        rawTrace: trace,
        sourceFramework: "dotnet"
      };
    } catch {}
  }

  // 2. .NET / C# Stack Traces (from VS Output Window, Debug Console, or Console logs)
  const dotNetExMatch = trimmed.match(/(?:Unhandled exception\.\s+)?([A-Za-z0-9_.]+(?:Exception|Error))(?::\s*([^\r\n]*))?/i);
  const dotNetLineMatch = trimmed.match(/in\s+([A-Za-z]:\\[^:\r\n]+|\/[^:\r\n]+):(line|zeile)\s+(\d+)/i);

  if (dotNetExMatch && (dotNetLineMatch || trimmed.includes("   at "))) {
    const filePath = dotNetLineMatch ? dotNetLineMatch[1].trim() : undefined;
    return {
      exceptionType: dotNetExMatch[1],
      message: (dotNetExMatch[2] || "").trim(),
      filePath,
      fileName: filePath ? extractFileName(filePath) : undefined,
      lineNumber: dotNetLineMatch ? parseInt(dotNetLineMatch[3], 10) : undefined,
      rawTrace: trimmed,
      sourceFramework: "dotnet"
    };
  }

  // 3. C++ / MSVC Debugger Break (Access Violation, Assertion Failed, CRT error)
  const cppMatch = trimmed.match(/Exception thrown at (0x[0-9A-Fa-f]+).*?:\s*(0x[0-9A-Fa-f]+|[^:\r\n]+):\s*([^\r\n]+)/i) ||
                   trimmed.match(/Assertion failed:\s*([^,\r\n]+),\s*file\s+([^,\r\n]+),\s*line\s+(\d+)/i);

  if (cppMatch) {
    if (cppMatch[2] && cppMatch[3] && !isNaN(Number(cppMatch[3]))) {
      // Assertion failed format
      const fPath = cppMatch[2].trim();
      return {
        exceptionType: "Assertion Failure",
        message: cppMatch[1].trim(),
        filePath: fPath,
        fileName: extractFileName(fPath),
        lineNumber: parseInt(cppMatch[3], 10),
        rawTrace: trimmed,
        sourceFramework: "cpp"
      };
    } else {
      // Access violation / SEH exception
      const fileLineMatch = trimmed.match(/at\s+([A-Za-z]:\\[^:\r\n]+|\/[^:\r\n]+):(\d+)/i);
      const fPath = fileLineMatch ? fileLineMatch[1].trim() : undefined;
      return {
        exceptionType: cppMatch[2] || "C++ Runtime Exception",
        message: cppMatch[3] || cppMatch[0],
        filePath: fPath,
        fileName: fPath ? extractFileName(fPath) : undefined,
        lineNumber: fileLineMatch ? parseInt(fileLineMatch[2], 10) : undefined,
        rawTrace: trimmed,
        sourceFramework: "cpp"
      };
    }
  }

  // 4. Node.js / TypeScript / JavaScript Stack Traces
  const jsMatch = trimmed.match(/^([A-Za-z0-9_.]*(?:Error|Exception)):\s*([^\r\n]*)/m);
  const jsLineMatch = trimmed.match(/\((([A-Za-z]:\\[^:()]+|\/[^:()]+)):(\d+):(\d+)\)/) ||
                      trimmed.match(/at\s+(([A-Za-z]:\\[^:\s]+|\/[^:\s]+)):(\d+):(\d+)/);

  if (jsMatch && jsLineMatch) {
    const fPath = jsLineMatch[1].trim();
    return {
      exceptionType: jsMatch[1],
      message: (jsMatch[2] || "").trim(),
      filePath: fPath,
      fileName: extractFileName(fPath),
      lineNumber: parseInt(jsLineMatch[3], 10),
      columnNumber: parseInt(jsLineMatch[4], 10),
      rawTrace: trimmed,
      sourceFramework: "node"
    };
  }

  // 5. Python Traceback
  if (trimmed.includes("Traceback (most recent call last):")) {
    const pyLineMatches = Array.from(trimmed.matchAll(/File\s+"([^"]+)",\s+line\s+(\d+)/g));
    const lastLineMatch = pyLineMatches[pyLineMatches.length - 1];
    const pyExMatch = trimmed.match(/([A-Za-z0-9_]+Error):\s*([^\r\n]*)$/m);

    const fPath = lastLineMatch ? lastLineMatch[1] : undefined;
    return {
      exceptionType: pyExMatch ? pyExMatch[1] : "Python Exception",
      message: pyExMatch ? pyExMatch[2].trim() : "Traceback captured",
      filePath: fPath,
      fileName: fPath ? extractFileName(fPath) : undefined,
      lineNumber: lastLineMatch ? parseInt(lastLineMatch[2], 10) : undefined,
      rawTrace: trimmed,
      sourceFramework: "python"
    };
  }

  // 6. Rust Panic
  const rustPanicMatch = trimmed.match(/thread '([^']+)' panicked at\s*(?:'([^']+)',\s*)?([^\s:]+):(\d+):(\d+)/);
  if (rustPanicMatch) {
    const fPath = rustPanicMatch[3];
    return {
      exceptionType: "Rust Panic",
      message: rustPanicMatch[2] || `Panic on thread '${rustPanicMatch[1]}'`,
      filePath: fPath,
      fileName: extractFileName(fPath),
      lineNumber: parseInt(rustPanicMatch[4], 10),
      columnNumber: parseInt(rustPanicMatch[5], 10),
      rawTrace: trimmed,
      sourceFramework: "rust"
    };
  }

  return null;
}

/**
 * Formats a high-context diagnostic prompt for Gemini from the parsed error and local code snippet
 */
export function formatDebugPrompt(error: ParsedDebugError, snippet?: FileSnippetResult | null): string {
  const fileLabel = error.fileName || (error.filePath ? extractFileName(error.filePath) : "Unknown File");
  const lineLabel = error.lineNumber ? ` (Line ${error.lineNumber})` : "";
  const lang = snippet?.language || "csharp";

  let prompt = `I hit a runtime exception during debugging. Please analyze the root cause and provide the exact fix.\n\n`;
  prompt += `### 🐛 Debug Exception Details\n`;
  prompt += `- **Exception Type:** \`${error.exceptionType}\`\n`;
  if (error.message) {
    prompt += `- **Message:** ${error.message}\n`;
  }
  if (error.filePath || error.fileName) {
    prompt += `- **Location:** \`${fileLabel}\`${lineLabel}\n`;
    if (error.filePath && error.filePath !== fileLabel) {
      prompt += `- **Full Path:** \`${error.filePath}\`\n`;
    }
  }

  if (snippet && snippet.exists && snippet.snippet) {
    prompt += `\n### 📄 Source Code Context (\`${fileLabel}\` around line ${snippet.target_line})\n`;
    prompt += `\`\`\`${lang}\n${snippet.snippet}\n\`\`\`\n`;
  }

  prompt += `\n<details>\n<summary><b>View Stack Trace / Debugger Output</b></summary>\n\n\`\`\`text\n${error.rawTrace.trim()}\n\`\`\`\n</details>\n\n`;
  prompt += `**Tasks:**\n`;
  prompt += `1. **Identify Root Cause**: Explain why this exception occurred at the indicated line.\n`;
  prompt += `2. **Provide Code Fix**: Show the revised code snippet with null checks, bounds validation, or appropriate error handling.\n`;
  prompt += `3. **Preventative Advice**: Suggest any defensive coding practices or pattern improvements to prevent recurrence.`;

  return prompt;
}
