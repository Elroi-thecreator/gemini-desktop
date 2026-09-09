use std::process::{Child, Command, Stdio};
use std::path::PathBuf;

#[cfg(target_os = "windows")]
use windows_sys::Win32::Foundation::HANDLE;
#[cfg(target_os = "windows")]
use windows_sys::Win32::System::JobObjects::{
    CreateJobObjectW, SetInformationJobObject, AssignProcessToJobObject,
    JobObjectExtendedLimitInformation, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
};
#[cfg(target_os = "windows")]
use std::os::windows::io::AsRawHandle;

pub struct ProcessSupervisor {
    #[cfg(target_os = "windows")]
    job_handle: Option<HANDLE>,
}

#[cfg(target_os = "windows")]
unsafe impl Send for ProcessSupervisor {}
#[cfg(target_os = "windows")]
unsafe impl Sync for ProcessSupervisor {}

impl ProcessSupervisor {
    pub fn new() -> Self {
        #[cfg(target_os = "windows")]
        {
            let job = unsafe {
                let job = CreateJobObjectW(std::ptr::null(), std::ptr::null());
                if !job.is_null() {
                    let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    let result = SetInformationJobObject(
                        job,
                        JobObjectExtendedLimitInformation,
                        &info as *const _ as *const _,
                        std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
                    );
                    if result != 0 {
                        Some(job)
                    } else {
                        None
                    }
                } else {
                    None
                }
            };
            Self { job_handle: job }
        }

        #[cfg(not(target_os = "windows"))]
        {
            Self {}
        }
    }

    pub fn spawn_gemini(
        &self,
        gemini_binary: &str,
        working_dir: Option<PathBuf>,
        extra_args: &[String],
    ) -> Result<Child, String> {
        let mut cmd = Command::new(gemini_binary);
        cmd.arg("--acp");

        for arg in extra_args {
            cmd.arg(arg);
        }

        if let Some(dir) = working_dir {
            if dir.exists() {
                cmd.current_dir(dir);
            }
        }

        cmd.stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        #[cfg(target_os = "windows")]
        {
            use std::os::windows::process::CommandExt;
            cmd.creation_flags(0x08000000); // CREATE_NO_WINDOW
        }

        let child = cmd.spawn().map_err(|e| format!("Failed to spawn gemini CLI ({}): {}", gemini_binary, e))?;

        #[cfg(target_os = "windows")]
        {
            if let Some(job) = self.job_handle {
                let process_handle = child.as_raw_handle();
                unsafe {
                    AssignProcessToJobObject(job, process_handle as _);
                }
            }
        }

        Ok(child)
    }
}
