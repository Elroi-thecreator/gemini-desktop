import { invoke } from "@tauri-apps/api/core";
import { onAction } from "@tauri-apps/plugin-notification";

export const NOTIFICATIONS_ENABLED_KEY = "gemini_desktop_notifications_enabled";

export function isNotificationSettingEnabled(): boolean {
  if (typeof window === "undefined") return true;
  const val = localStorage.getItem(NOTIFICATIONS_ENABLED_KEY);
  return val === null ? true : val === "true";
}

export function setNotificationSetting(enabled: boolean): void {
  if (typeof window === "undefined") return;
  localStorage.setItem(NOTIFICATIONS_ENABLED_KEY, enabled ? "true" : "false");
}

let actionListenerInitialized = false;
export function setupNotificationActionListener() {
  if (actionListenerInitialized) return;
  actionListenerInitialized = true;
  try {
    onAction(async () => {
      try {
        await invoke("focus_app_window");
      } catch (e) {
        console.warn("Failed to focus app window:", e);
      }
    });
  } catch (e) {
    console.warn("Could not register onAction listener:", e);
  }
}

/**
 * Sends a native Windows toast notification via the custom Windows WinRT bridge.
 * Sets the exact AUMID "com.geminidesktop.app" so the header shows "Gemini Desktop"
 * and clicking the notification unminimizes and activates the Gemini Desktop window!
 */
export async function notifyBackgroundTaskComplete(
  title: string,
  body: string,
  force: boolean = false
): Promise<boolean> {
  try {
    if (!force && !isNotificationSettingEnabled()) {
      return false;
    }

    // Only notify if window is in background / blurred / minimized, unless force is true
    if (!force && typeof document !== "undefined" && document.hasFocus()) {
      return false;
    }

    await invoke("show_toast_notification", { title, body });
    return true;
  } catch (e) {
    console.warn("Failed to send native toast notification:", e);
    return false;
  }
}
