export interface DialogOptions {
  title?: string;
  confirmText?: string;
  cancelText?: string;
  isDestructive?: boolean;
  placeholder?: string;
}

class DialogManager {
  isOpen = $state(false);
  title = $state("Gemini Desktop");
  message = $state("");
  type = $state<"alert" | "confirm" | "prompt">("alert");
  confirmText = $state("OK");
  cancelText = $state("Cancel");
  inputValue = $state("");
  placeholder = $state("");
  isDestructive = $state(false);
  private resolveFn: ((val: any) => void) | null = null;

  alert(message: string, title: string = "Gemini Desktop"): Promise<void> {
    return new Promise((resolve) => {
      this.title = title;
      this.message = message;
      this.type = "alert";
      this.confirmText = "OK";
      this.cancelText = "Cancel";
      this.isDestructive = false;
      this.resolveFn = () => resolve();
      this.isOpen = true;
    });
  }

  confirm(message: string, options?: DialogOptions): Promise<boolean> {
    return new Promise((resolve) => {
      this.title = options?.title || "Gemini Desktop";
      this.message = message;
      this.type = "confirm";
      this.confirmText = options?.confirmText || "Confirm";
      this.cancelText = options?.cancelText || "Cancel";
      this.isDestructive = options?.isDestructive ?? false;
      this.resolveFn = (val) => resolve(!!val);
      this.isOpen = true;
    });
  }

  prompt(message: string, defaultValue: string = "", options?: DialogOptions): Promise<string | null> {
    return new Promise((resolve) => {
      this.title = options?.title || "Gemini Desktop";
      this.message = message;
      this.type = "prompt";
      this.inputValue = defaultValue;
      this.placeholder = options?.placeholder || "";
      this.confirmText = options?.confirmText || "OK";
      this.cancelText = options?.cancelText || "Cancel";
      this.isDestructive = false;
      this.resolveFn = (val) => resolve(val);
      this.isOpen = true;
    });
  }

  handleConfirm() {
    this.isOpen = false;
    if (this.type === "prompt") {
      this.resolveFn?.(this.inputValue);
    } else {
      this.resolveFn?.(true);
    }
    this.resolveFn = null;
  }

  handleCancel() {
    this.isOpen = false;
    if (this.type === "prompt") {
      this.resolveFn?.(null);
    } else {
      this.resolveFn?.(false);
    }
    this.resolveFn = null;
  }
}

export const dialogManager = new DialogManager();
