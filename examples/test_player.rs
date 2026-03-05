use rustav_native::Logging::Debug::{Debug, Initialize, Teardown};
use rustav_native::Player::Player;
use rustav_native::SDLWindow::SDLWindow;
use rustav_native::TextureClient::TextureClient;
use sdl2_sys as sdl;
use std::sync::Mutex;
use std::thread;
use std::time::{Duration, Instant};

lazy_static::lazy_static! {
    static ref G_MUTEX: Mutex<()> = Mutex::new(());
    static ref MEMORY_CHECKPOINT_WORKING_SET: Mutex<Option<usize>> = Mutex::new(None);
}

unsafe fn run_test_player(uri: &str, x: i32, y: i32, w: i32, h: i32) {
    let _setup_lock = G_MUTEX.lock().unwrap();
    let _window = SDLWindow::new(uri.to_string(), x, y, w, h);
}

unsafe fn create_players(uris: &[String], windows: &[SDLWindow]) -> Option<Vec<Player>> {
    let mut players = Vec::new();

    for (i, uri) in uris.iter().enumerate() {
        let client = TextureClient::from_sdl_window(&windows[i]);
        let player = Player::Create(uri.clone(), Box::new(client))?;
        players.push(player);
    }

    Some(players)
}

unsafe fn create_windows(
    uris: &[String],
    w: i32,
    h: i32,
    xp: i32,
    yp: i32,
) -> Option<Vec<SDLWindow>> {
    let mut windows = Vec::new();

    for (i, uri) in uris.iter().enumerate() {
        let window = SDLWindow::new(uri.clone(), xp * (i as i32 + 1) + i as i32 * w, yp, w, h)?;

        windows.push(window);
    }

    Some(windows)
}

unsafe fn create_players_and_run(
    uris: &[String],
    w: i32,
    h: i32,
    xp: i32,
    yp: i32,
    loop_players: bool,
    max_seconds: Option<f64>,
) -> bool {
    let mut windows = match create_windows(uris, w, h, xp, yp) {
        Some(v) if !v.is_empty() => v,
        _ => return false,
    };

    let mut players = match create_players(uris, &windows) {
        Some(v) if !v.is_empty() => v,
        _ => return false,
    };

    let mut quit = false;
    let start = Instant::now();

    for player in players.iter_mut() {
        player.Play();
        if loop_players {
            player.SetLoop(true);
        }
    }

    while !quit {
        if let Some(limit) = max_seconds {
            if limit > 0.0 && start.elapsed().as_secs_f64() >= limit {
                break;
            }
        }

        let mut event = std::mem::zeroed::<sdl::SDL_Event>();
        while sdl::SDL_PollEvent(&mut event) != 0 {
            if event.type_ == sdl::SDL_EventType::SDL_WINDOWEVENT as u32 {
                let window_id = event.window.windowID;

                let mut remove_index: Option<usize> = None;
                for i in 0..windows.len() {
                    if windows[i].WindowId() == window_id {
                        if event.window.event as u32
                            == sdl::SDL_WindowEventID::SDL_WINDOWEVENT_CLOSE as u32
                        {
                            remove_index = Some(i);
                        } else {
                            windows[i].HandleEvent(event);
                        }
                        break;
                    }
                }

                if let Some(i) = remove_index {
                    windows.remove(i);
                    players.remove(i);
                    if windows.is_empty() {
                        quit = true;
                    }
                }
            }
        }

        for player in players.iter_mut() {
            player.Write();
        }

        thread::sleep(Duration::from_micros(50_000));
    }

    true
}

unsafe fn run_test(uris: &[String], loop_players: bool) {
    if sdl::SDL_Init((sdl::SDL_INIT_VIDEO | sdl::SDL_INIT_EVENTS) as u32) < 0 {
        Debug::Log("SDL Failed to initialize");
        return;
    }

    let width = 640;
    let height = 400;
    let x_padding = 30;
    let y_padding = 60;

    let _ = create_players_and_run(
        uris,
        width,
        height,
        x_padding,
        y_padding,
        loop_players,
        None,
    );

    sdl::SDL_Quit();
}

unsafe fn allocation_test(rtsp_uri: &str) {
    let client = TextureClient::from_null_writer(1280, 800);
    let _ = Player::Create(rtsp_uri.to_string(), Box::new(client));
}

unsafe fn rtsp_test(multiple: bool) {
    let mut uris = vec!["rtsp://localhost:554/stream0".to_string()];

    if multiple {
        uris.push("rtsp://localhost:555/stream1".to_string());
        uris.push("rtsp://localhost:556/stream2".to_string());
        uris.push("rtsp://localhost:557/stream3".to_string());
    }

    run_test(&uris, false);
}

unsafe fn file_test(loop_players: bool) {
    let uris = vec![sample_video_uri()];
    run_test(&uris, loop_players);
}

unsafe fn file_test_invalid_uri() {
    let uris = vec!["invaliduri.invaliduri".to_string()];
    run_test(&uris, false);
}

fn sample_video_uri() -> String {
    let candidates = [
        "../TestFiles/SampleVideo_1280x720_10mb.mp4",
        "TestFiles/SampleVideo_1280x720_10mb.mp4",
        "./TestFiles/SampleVideo_1280x720_10mb.mp4",
    ];

    for c in candidates {
        if std::path::Path::new(c).exists() {
            return c.to_string();
        }
    }

    candidates[0].to_string()
}

#[cfg(windows)]
unsafe fn current_working_set_size() -> Option<usize> {
    use windows::Win32::System::ProcessStatus::{K32GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS};
    use windows::Win32::System::Threading::GetCurrentProcess;

    let process = GetCurrentProcess();
    let mut counters = PROCESS_MEMORY_COUNTERS::default();
    let ok = K32GetProcessMemoryInfo(
        process,
        &mut counters as *mut _ as *mut _,
        std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32,
    )
    .as_bool();

    if ok {
        Some(counters.WorkingSetSize)
    } else {
        None
    }
}

#[cfg(windows)]
unsafe fn run_memory_checkpoint() {
    let mut checkpoint = MEMORY_CHECKPOINT_WORKING_SET.lock().unwrap();
    *checkpoint = current_working_set_size();
}

#[cfg(windows)]
unsafe fn dump_memory_checkpoint() {
    let checkpoint = MEMORY_CHECKPOINT_WORKING_SET.lock().unwrap();
    let Some(base) = *checkpoint else {
        return;
    };

    let Some(current) = current_working_set_size() else {
        return;
    };

    let delta = current as i64 - base as i64;
    Debug::Log(&format!(
        "[MemoryCheckpoint] working_set_base={} current={} delta={}",
        base, current, delta
    ));
}

#[cfg(not(windows))]
unsafe fn run_memory_checkpoint() {}

#[cfg(not(windows))]
unsafe fn dump_memory_checkpoint() {}

fn main() {
    if std::env::args().len() == 1 {
        unsafe {
            run_memory_checkpoint();
            Initialize(false);
            file_test(true);
            Teardown();
            dump_memory_checkpoint();
        }
        return;
    }

    fn print_usage() {
        println!(
            "Usage: test_player [--case=<name>] [--max-seconds=<seconds>] [--uri=<stream_uri>]"
        );
        println!(
            "Cases: file_loop, file_once, invalid_uri, alloc_stream, alloc_rtsp, alloc_rtmp, rtsp_single, rtmp_single, rtsp_multi"
        );
    }

    let mut case = String::from("file_loop");
    let mut max_seconds: Option<f64> = None;
    let mut stream_uri = String::from("rtsp://localhost:554/stream0");
    let mut parse_error = false;
    let mut show_help = false;

    for arg in std::env::args().skip(1) {
        if arg == "--help" || arg == "-h" {
            show_help = true;
            continue;
        }

        if let Some(v) = arg.strip_prefix("--case=") {
            case = v.to_string();
            continue;
        }

        if let Some(v) = arg.strip_prefix("--max-seconds=") {
            match v.parse::<f64>() {
                Ok(parsed) => max_seconds = Some(parsed),
                Err(_) => {
                    eprintln!("Invalid value for --max-seconds: {}", v);
                    parse_error = true;
                }
            }
            continue;
        }

        if let Some(v) = arg.strip_prefix("--uri=") {
            stream_uri = v.to_string();
            continue;
        }

        eprintln!("Unknown argument: {}", arg);
        parse_error = true;
    }

    if show_help {
        print_usage();
        std::process::exit(0);
    }

    if parse_error {
        print_usage();
        std::process::exit(2);
    }

    let run_result = |case_name: &str, max_secs: Option<f64>, stream_uri_arg: &str| -> i32 {
        unsafe fn run_case_with_sdl(
            uris: Vec<String>,
            loop_players: bool,
            max_secs: Option<f64>,
            require_create_success: bool,
        ) -> i32 {
            if sdl::SDL_Init((sdl::SDL_INIT_VIDEO | sdl::SDL_INIT_EVENTS) as u32) < 0 {
                Debug::Log("SDL Failed to initialize");
                return 1;
            }

            let started = create_players_and_run(&uris, 640, 400, 30, 60, loop_players, max_secs);
            sdl::SDL_Quit();

            if started == require_create_success {
                0
            } else {
                1
            }
        }

        unsafe {
            match case_name {
                "file_loop" => run_case_with_sdl(vec![sample_video_uri()], true, max_secs, true),
                "file_once" => run_case_with_sdl(vec![sample_video_uri()], false, max_secs, true),
                "invalid_uri" => run_case_with_sdl(
                    vec!["invaliduri.invaliduri".to_string()],
                    false,
                    max_secs,
                    false,
                ),
                "alloc_stream" | "alloc_rtsp" | "alloc_rtmp" => {
                    allocation_test(stream_uri_arg);
                    0
                }
                "rtsp_single" => {
                    run_case_with_sdl(vec![stream_uri_arg.to_string()], false, max_secs, true)
                }
                "rtmp_single" => {
                    run_case_with_sdl(vec![stream_uri_arg.to_string()], false, max_secs, true)
                }
                "rtsp_multi" => run_case_with_sdl(
                    vec![
                        "rtsp://localhost:554/stream0".to_string(),
                        "rtsp://localhost:555/stream1".to_string(),
                        "rtsp://localhost:556/stream2".to_string(),
                        "rtsp://localhost:557/stream3".to_string(),
                    ],
                    false,
                    max_secs,
                    true,
                ),
                _ => {
                    print_usage();
                    2
                }
            }
        }
    };

    unsafe {
        run_memory_checkpoint();
        Initialize(false);

        let code = run_result(&case, max_seconds, &stream_uri);

        Teardown();

        dump_memory_checkpoint();

        if code != 0 {
            std::process::exit(code);
        }
    }
}
