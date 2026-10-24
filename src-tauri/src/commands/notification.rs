use tauri::Manager;
use tauri_winrt_notification::Toast;

pub const AUMID: &str = "com.geminidesktop.app";

/// Configures the Application User Model ID (AUMID) on Windows in-process without spawning
/// external processes like reg.exe, ensuring 100% compliance with corporate EDR standards (CrowdStrike Falcon).
pub fn ensure_windows_aumid() {
    #[cfg(windows)]
    {
        use std::ffi::OsStr;
        use std::os::windows::ffi::OsStrExt;
        use windows_sys::Win32::System::LibraryLoader::{GetModuleHandleA, GetProcAddress};
        use windows_sys::Win32::System::Registry::{
            RegCloseKey, RegCreateKeyExW, RegSetValueExW, HKEY, HKEY_CURRENT_USER, KEY_SET_VALUE,
            REG_OPTION_NON_VOLATILE, REG_SZ,
        };

        // 1. Set explicit AppUserModelID in current process memory via shell32
        unsafe {
            let shell32 = GetModuleHandleA(b"shell32.dll\0".as_ptr());
            if !shell32.is_null() {
                let func_ptr =
                    GetProcAddress(shell32, b"SetCurrentProcessExplicitAppUserModelID\0".as_ptr());
                if let Some(set_aumid) = func_ptr {
                    type SetAumidFn = unsafe extern "system" fn(*const u16) -> i32;
                    let set_aumid: SetAumidFn = std::mem::transmute(set_aumid);
                    let aumid_wide: Vec<u16> =
                        OsStr::new(AUMID).encode_wide().chain(Some(0)).collect();
                    let _ = set_aumid(aumid_wide.as_ptr());
                }
            }
        }

        // 2. Direct Win32 in-process registry configuration (pure native API, zero child process creation)
        unsafe {
            let subkey: Vec<u16> = OsStr::new(r"Software\Classes\AppUserModelId\com.geminidesktop.app")
                .encode_wide()
                .chain(Some(0))
                .collect();

            let mut hkey: HKEY = std::ptr::null_mut();
            let res = RegCreateKeyExW(
                HKEY_CURRENT_USER,
                subkey.as_ptr(),
                0,
                std::ptr::null_mut(),
                REG_OPTION_NON_VOLATILE,
                KEY_SET_VALUE,
                std::ptr::null_mut(),
                &mut hkey,
                std::ptr::null_mut(),
            );

            if res == 0 && !hkey.is_null() {
                let val_name: Vec<u16> =
                    OsStr::new("DisplayName").encode_wide().chain(Some(0)).collect();
                let val_data: Vec<u16> =
                    OsStr::new("Gemini Desktop").encode_wide().chain(Some(0)).collect();
                let _ = RegSetValueExW(
                    hkey,
                    val_name.as_ptr(),
                    0,
                    REG_SZ,
                    val_data.as_ptr() as *const u8,
                    (val_data.len() * 2) as u32,
                );

                let bg_name: Vec<u16> =
                    OsStr::new("IconBackgroundColor").encode_wide().chain(Some(0)).collect();
                let bg_data: Vec<u16> = OsStr::new("0").encode_wide().chain(Some(0)).collect();
                let _ = RegSetValueExW(
                    hkey,
                    bg_name.as_ptr(),
                    0,
                    REG_SZ,
                    bg_data.as_ptr() as *const u8,
                    (bg_data.len() * 2) as u32,
                );

                if let Ok(exe_path) = std::env::current_exe() {
                    let icon_name: Vec<u16> =
                        OsStr::new("IconUri").encode_wide().chain(Some(0)).collect();
                    let icon_data: Vec<u16> =
                        OsStr::new(&exe_path).encode_wide().chain(Some(0)).collect();
                    let _ = RegSetValueExW(
                        hkey,
                        icon_name.as_ptr(),
                        0,
                        REG_SZ,
                        icon_data.as_ptr() as *const u8,
                        (icon_data.len() * 2) as u32,
                    );
                }

                RegCloseKey(hkey);
            }
        }
    }
}

#[tauri::command]
pub fn show_toast_notification(
    app_handle: tauri::AppHandle,
    title: String,
    body: String,
) -> Result<(), String> {
    let handle = app_handle.clone();

    std::thread::spawn(move || {
        let mut toast = Toast::new(AUMID);
        toast = toast.title(&title).text1(&body);

        let handle_clone = handle.clone();
        toast = toast.on_activated(move |_action| {
            if let Some(window) = handle_clone.get_webview_window("main") {
                let _ = window.unminimize();
                let _ = window.show();
                let _ = window.set_focus();

                #[cfg(windows)]
                unsafe {
                    use windows_sys::Win32::UI::WindowsAndMessaging::{
                        SetForegroundWindow, ShowWindow, SW_RESTORE,
                    };
                    if let Ok(hwnd) = window.hwnd() {
                        let raw_hwnd = hwnd.0 as *mut std::ffi::c_void;
                        ShowWindow(raw_hwnd, SW_RESTORE);
                        SetForegroundWindow(raw_hwnd);
                    }
                }
            }
            Ok(())
        });

        if let Err(e) = toast.show() {
            eprintln!("Failed to display Windows toast notification: {e:?}");
        }
    });

    Ok(())
}

#[tauri::command]
pub fn focus_app_window(app_handle: tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app_handle.get_webview_window("main") {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();

        #[cfg(windows)]
        unsafe {
            use windows_sys::Win32::UI::WindowsAndMessaging::{
                SetForegroundWindow, ShowWindow, SW_RESTORE,
            };
            if let Ok(hwnd) = window.hwnd() {
                let raw_hwnd = hwnd.0 as *mut std::ffi::c_void;
                ShowWindow(raw_hwnd, SW_RESTORE);
                SetForegroundWindow(raw_hwnd);
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_aumid_identifier() {
        assert_eq!(AUMID, "com.geminidesktop.app");
    }
}
