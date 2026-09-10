export type ThemeId =
  | "platinum-dark"
  | "platinum-white"
  | "gemini-aurora"
  | "cyber-emerald";

export interface ThemeInfo {
  id: ThemeId;
  name: string;
  tagline: string;
  accent: string;
  bgPreview: string;
  surfacePreview: string;
  borderPreview: string;
}

export const THEMES: ThemeInfo[] = [
  {
    id: "platinum-dark",
    name: "Platinum Dark (Default)",
    tagline: "Deep Slate & Royal Blue",
    accent: "#3b82f6",
    bgPreview: "#0b0f19",
    surfacePreview: "#172033",
    borderPreview: "#2d3b55",
  },
  {
    id: "platinum-white",
    name: "Platinum Light",
    tagline: "Crisp White & Slate Grey",
    accent: "#2563eb",
    bgPreview: "#f8fafc",
    surfacePreview: "#ffffff",
    borderPreview: "#cbd5e1",
  },
  {
    id: "gemini-aurora",
    name: "Gemini Aurora",
    tagline: "Cosmic Indigo & Violet Glow",
    accent: "#8b5cf6",
    bgPreview: "#090b14",
    surfacePreview: "#111424",
    borderPreview: "#222744",
  },
  {
    id: "cyber-emerald",
    name: "Cyber Emerald",
    tagline: "Abyssal Pine & Vivid Mint",
    accent: "#10b981",
    bgPreview: "#081210",
    surfacePreview: "#0e1e1b",
    borderPreview: "#193832",
  },
];

class ThemeManager {
  current = $state<ThemeId>("platinum-dark");

  init() {
    if (typeof window === "undefined") return;
    const saved = localStorage.getItem("gemini_desktop_theme") as ThemeId | null;
    if (saved && THEMES.some((t) => t.id === saved)) {
      this.current = saved;
    } else {
      this.current = "platinum-dark";
    }
    this.apply();
  }

  setTheme(id: ThemeId) {
    this.current = id;
    if (typeof window !== "undefined") {
      localStorage.setItem("gemini_desktop_theme", id);
      this.apply();
    }
  }

  private apply() {
    if (typeof document !== "undefined") {
      document.documentElement.setAttribute("data-theme", this.current);
    }
  }
}

export const themeManager = new ThemeManager();
