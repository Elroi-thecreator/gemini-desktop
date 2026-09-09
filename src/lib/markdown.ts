import { marked } from "marked";
import hljs from "highlight.js";

// Custom renderer to format code blocks with language pills and copy buttons
const renderer = new marked.Renderer();

renderer.code = function ({ text, lang }) {
  const validLang = lang && hljs.getLanguage(lang) ? lang : "plaintext";
  let highlighted = "";
  try {
    highlighted = hljs.highlight(text, { language: validLang }).value;
  } catch {
    highlighted = text;
  }

  const encodedRaw = encodeURIComponent(text);

  return `
    <div class="code-block my-3 rounded-lg overflow-hidden border border-slate-800 bg-slate-950 font-mono text-xs">
      <div class="flex items-center justify-between px-3 py-1.5 bg-slate-900/80 border-b border-slate-800 text-slate-400">
        <span class="text-[11px] uppercase font-semibold tracking-wider text-sky-400">${validLang}</span>
        <button 
          class="copy-code-btn px-2 py-0.5 rounded text-[11px] bg-slate-800 hover:bg-slate-700 text-slate-300 transition-colors"
          data-code="${encodedRaw}"
          onclick="
            const code = decodeURIComponent(this.getAttribute('data-code'));
            navigator.clipboard.writeText(code);
            const orig = this.innerText;
            this.innerText = 'Copied!';
            setTimeout(() => { this.innerText = orig; }, 1500);
          "
        >
          Copy
        </button>
      </div>
      <pre class="p-3 overflow-x-auto text-slate-200"><code>${highlighted}</code></pre>
    </div>
  `;
};

marked.setOptions({
  renderer,
  gfm: true,
  breaks: true,
});

export function renderMarkdown(content: string): string {
  if (!content) return "";
  try {
    return marked.parse(content) as string;
  } catch (e) {
    return content;
  }
}
