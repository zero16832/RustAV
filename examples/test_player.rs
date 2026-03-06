#[cfg(not(windows))]
fn main() {
    eprintln!("test_player 仅支持 Windows");
    std::process::exit(1);
}

#[cfg(windows)]
mod win32_player {
    use rustav_native::FrameExportClient::SharedExportedFrameState;
    use rustav_native::Logging::Debug::{Initialize, Teardown};
    use rustav_native::Player::Player;
    use std::ffi::c_void;
    use std::mem::size_of;
    use std::sync::{Arc, Mutex, OnceLock};
    use std::thread;
    use std::time::{Duration, Instant};
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, RECT, WPARAM};
    use windows::Win32::Graphics::Gdi::{
        BeginPaint, EndPaint, InvalidateRect, StretchDIBits, UpdateWindow, BITMAPINFO,
        BITMAPINFOHEADER, BI_RGB, DIB_RGB_COLORS, HBRUSH, PAINTSTRUCT, SRCCOPY,
    };
    use windows::Win32::System::LibraryLoader::GetModuleHandleW;
    use windows::Win32::UI::WindowsAndMessaging::{
        AdjustWindowRectEx, CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW,
        GetClientRect, LoadCursorW, PeekMessageW, PostQuitMessage, RegisterClassW, ShowWindow,
        TranslateMessage, CS_HREDRAW, CS_VREDRAW, CW_USEDEFAULT, HMENU, IDC_ARROW, MSG,
        PM_REMOVE, SW_SHOW, WINDOW_EX_STYLE, WM_CLOSE, WM_DESTROY, WM_PAINT, WM_QUIT, WNDCLASSW,
        WS_OVERLAPPEDWINDOW, WS_VISIBLE,
    };

    static VIEWER_STATE: OnceLock<Arc<Mutex<ViewerState>>> = OnceLock::new();

    struct Config {
        uri: String,
        width: i32,
        height: i32,
        max_seconds: Option<f64>,
        loop_player: bool,
    }

    struct ViewerState {
        source_width: i32,
        source_height: i32,
        stride: i32,
        rgba_buffer: Vec<u8>,
        bgra_buffer: Vec<u8>,
        has_frame: bool,
        last_frame_index: i64,
        last_time_sec: f64,
        title: String,
    }

    impl ViewerState {
        fn new(width: i32, height: i32, title: String) -> Self {
            let pixel_bytes = width.saturating_mul(height).saturating_mul(4).max(0) as usize;
            Self {
                source_width: width,
                source_height: height,
                stride: width.saturating_mul(4),
                rgba_buffer: vec![0; pixel_bytes],
                bgra_buffer: vec![0; pixel_bytes],
                has_frame: false,
                last_frame_index: -1,
                last_time_sec: 0.0,
                title,
            }
        }
    }

    fn parse_args() -> Result<Config, String> {
        let mut uri = sample_video_uri();
        let mut width = 1280;
        let mut height = 720;
        let mut max_seconds = None;
        let mut loop_player = false;

        for arg in std::env::args().skip(1) {
            if arg == "--help" || arg == "-h" {
                print_usage();
                std::process::exit(0);
            }

            if arg == "--loop" {
                loop_player = true;
                continue;
            }

            if let Some(v) = arg.strip_prefix("--uri=") {
                uri = v.to_string();
                continue;
            }

            if let Some(v) = arg.strip_prefix("--width=") {
                width = v
                    .parse::<i32>()
                    .map_err(|_| format!("无效的 --width 参数: {v}"))?;
                continue;
            }

            if let Some(v) = arg.strip_prefix("--height=") {
                height = v
                    .parse::<i32>()
                    .map_err(|_| format!("无效的 --height 参数: {v}"))?;
                continue;
            }

            if let Some(v) = arg.strip_prefix("--max-seconds=") {
                max_seconds = Some(
                    v.parse::<f64>()
                        .map_err(|_| format!("无效的 --max-seconds 参数: {v}"))?,
                );
                continue;
            }

            return Err(format!("未知参数: {arg}"));
        }

        if width <= 0 || height <= 0 {
            return Err("width 和 height 必须大于 0".to_string());
        }

        Ok(Config {
            uri,
            width,
            height,
            max_seconds,
            loop_player,
        })
    }

    fn print_usage() {
        println!("Usage: test_player [--uri=<uri>] [--width=<w>] [--height=<h>] [--max-seconds=<n>] [--loop]");
        println!("默认使用 TestFiles 里的 sample video；也可直接传 rtsp:// 或 rtmp://");
    }

    fn sample_video_uri() -> String {
        let candidates = [
            "../TestFiles/SampleVideo_1280x720_10mb.mp4",
            "TestFiles/SampleVideo_1280x720_10mb.mp4",
            "./TestFiles/SampleVideo_1280x720_10mb.mp4",
        ];

        for candidate in candidates {
            if std::path::Path::new(candidate).exists() {
                return candidate.to_string();
            }
        }

        candidates[0].to_string()
    }

    fn to_wstring(value: &str) -> Vec<u16> {
        value.encode_utf16().chain(Some(0)).collect()
    }

    fn pull_latest_frame(
        shared: &SharedExportedFrameState,
        viewer_state: &Arc<Mutex<ViewerState>>,
    ) -> bool {
        let shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        let meta = shared.Meta();
        if !meta.HasFrame {
            return false;
        }

        let mut viewer = viewer_state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if meta.FrameIndex == viewer.last_frame_index {
            return false;
        }

        if meta.Width <= 0 || meta.Height <= 0 || meta.DataLength <= 0 {
            return false;
        }

        let data_len = meta.DataLength as usize;
        if viewer.rgba_buffer.len() != data_len {
            viewer.rgba_buffer.resize(data_len, 0);
        }
        if viewer.bgra_buffer.len() != data_len {
            viewer.bgra_buffer.resize(data_len, 0);
        }

        let copied = shared.CopyTo(&mut viewer.rgba_buffer);
        if copied <= 0 {
            return false;
        }

        let pixel_count = data_len / 4;
        for index in 0..pixel_count {
            let offset = index * 4;
            viewer.bgra_buffer[offset] = viewer.rgba_buffer[offset + 2];
            viewer.bgra_buffer[offset + 1] = viewer.rgba_buffer[offset + 1];
            viewer.bgra_buffer[offset + 2] = viewer.rgba_buffer[offset];
            viewer.bgra_buffer[offset + 3] = viewer.rgba_buffer[offset + 3];
        }

        viewer.source_width = meta.Width;
        viewer.source_height = meta.Height;
        viewer.stride = meta.Stride;
        viewer.has_frame = true;
        viewer.last_frame_index = meta.FrameIndex;
        viewer.last_time_sec = meta.Time;
        true
    }

    unsafe extern "system" fn window_proc(
        hwnd: HWND,
        msg: u32,
        wparam: WPARAM,
        lparam: LPARAM,
    ) -> LRESULT {
        match msg {
            WM_PAINT => {
                let mut paint = PAINTSTRUCT::default();
                let hdc = BeginPaint(hwnd, &mut paint);

                if let Some(viewer_state) = VIEWER_STATE.get() {
                    let viewer = viewer_state
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());

                    if viewer.has_frame && !viewer.bgra_buffer.is_empty() {
                        let mut info = BITMAPINFO::default();
                        info.bmiHeader = BITMAPINFOHEADER {
                            biSize: size_of::<BITMAPINFOHEADER>() as u32,
                            biWidth: viewer.source_width,
                            biHeight: -viewer.source_height,
                            biPlanes: 1,
                            biBitCount: 32,
                            biCompression: BI_RGB.0,
                            ..Default::default()
                        };

                        let mut rect = RECT::default();
                        let _ = GetClientRect(hwnd, &mut rect);
                        let dest_width = rect.right - rect.left;
                        let dest_height = rect.bottom - rect.top;

                        let _ = StretchDIBits(
                            hdc,
                            0,
                            0,
                            dest_width,
                            dest_height,
                            0,
                            0,
                            viewer.source_width,
                            viewer.source_height,
                            Some(viewer.bgra_buffer.as_ptr() as *const c_void),
                            &info,
                            DIB_RGB_COLORS,
                            SRCCOPY,
                        );
                    }
                }

                let _ = EndPaint(hwnd, &paint);
                LRESULT(0)
            }
            WM_CLOSE => {
                let _ = DestroyWindow(hwnd);
                LRESULT(0)
            }
            WM_DESTROY => {
                PostQuitMessage(0);
                LRESULT(0)
            }
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }

    unsafe fn create_window(title: &str, width: i32, height: i32) -> Result<HWND, String> {
        let instance = GetModuleHandleW(None).map_err(|e| format!("GetModuleHandleW failed: {e}"))?;
        let class_name = to_wstring("RustAVTestPlayerWindow");
        let title_w = to_wstring(title);

        let cursor = LoadCursorW(None, IDC_ARROW).map_err(|e| format!("LoadCursorW failed: {e}"))?;
        let window_class = WNDCLASSW {
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(window_proc),
            hInstance: instance.into(),
            lpszClassName: PCWSTR(class_name.as_ptr()),
            hCursor: cursor,
            hbrBackground: HBRUSH(0),
            ..Default::default()
        };

        if RegisterClassW(&window_class) == 0 {
            return Err("RegisterClassW failed".to_string());
        }

        let mut rect = RECT {
            left: 0,
            top: 0,
            right: width,
            bottom: height,
        };
        let _ = AdjustWindowRectEx(&mut rect, WS_OVERLAPPEDWINDOW, false, WINDOW_EX_STYLE(0));

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE(0),
            PCWSTR(class_name.as_ptr()),
            PCWSTR(title_w.as_ptr()),
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            rect.right - rect.left,
            rect.bottom - rect.top,
            HWND(0),
            HMENU(0),
            instance,
            None,
        );

        if hwnd.0 == 0 {
            return Err("CreateWindowExW failed".to_string());
        }

        ShowWindow(hwnd, SW_SHOW);
        let _ = UpdateWindow(hwnd);
        Ok(hwnd)
    }

    fn update_window_title(hwnd: HWND, viewer_state: &Arc<Mutex<ViewerState>>) {
        let viewer = viewer_state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());

        let title = format!(
            "{} | {}x{} | frame={} | t={:.3}s",
            viewer.title,
            viewer.source_width,
            viewer.source_height,
            viewer.last_frame_index,
            viewer.last_time_sec
        );
        let title_w = to_wstring(&title);

        unsafe {
            let _ = windows::Win32::UI::WindowsAndMessaging::SetWindowTextW(
                hwnd,
                PCWSTR(title_w.as_ptr()),
            );
        }
    }

    pub fn run() -> Result<(), String> {
        let config = parse_args()?;
        let title = format!("RustAV Test Player - {}", config.uri);

        Initialize(false);

        let (player, shared) =
            Player::CreateWithFrameExport(config.uri.clone(), config.width, config.height)
                .ok_or_else(|| format!("创建播放器失败: {}", config.uri))?;

        if config.loop_player {
            player.SetLoop(true);
        }
        player.Play();

        let viewer_state = Arc::new(Mutex::new(ViewerState::new(
            config.width,
            config.height,
            title.clone(),
        )));
        let _ = VIEWER_STATE.set(viewer_state.clone());

        let hwnd = unsafe { create_window(&title, config.width, config.height)? };
        let start = Instant::now();
        let mut quit = false;
        let mut last_title_update = Instant::now();

        while !quit {
            unsafe {
                let mut msg = MSG::default();
                while PeekMessageW(&mut msg, HWND(0), 0, 0, PM_REMOVE).into() {
                    if msg.message == WM_QUIT {
                        quit = true;
                        break;
                    }

                    let _ = TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }

            if quit {
                break;
            }

            if pull_latest_frame(&shared, &viewer_state) {
                unsafe {
                    let _ = InvalidateRect(hwnd, None, false);
                }

                if last_title_update.elapsed() >= Duration::from_millis(250) {
                    update_window_title(hwnd, &viewer_state);
                    last_title_update = Instant::now();
                }
            }

            if let Some(max_seconds) = config.max_seconds {
                if max_seconds > 0.0 && start.elapsed().as_secs_f64() >= max_seconds {
                    unsafe {
                        let _ = DestroyWindow(hwnd);
                    }
                }
            }

            thread::sleep(Duration::from_millis(15));
        }

        drop(player);
        Teardown();
        Ok(())
    }
}

#[cfg(windows)]
fn main() {
    if let Err(error) = win32_player::run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}
