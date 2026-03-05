use rustav_native::IVideoClient::IVideoClient;
use rustav_native::IVideoDescription::IVideoDescription;
use rustav_native::PixelFormat::PixelFormat;
use rustav_native::Player::Player;
use rustav_native::VideoFrame::VideoFrame;
use std::sync::{
    atomic::{AtomicUsize, Ordering},
    Arc,
};
use std::thread;
use std::time::{Duration, Instant};

struct ProbeVideoClient {
    width: i32,
    height: i32,
    format: PixelFormat,
    frames: Arc<AtomicUsize>,
}

impl IVideoDescription for ProbeVideoClient {
    fn Width(&self) -> i32 {
        self.width
    }

    fn Height(&self) -> i32 {
        self.height
    }

    fn Format(&self) -> PixelFormat {
        self.format
    }
}

impl IVideoClient for ProbeVideoClient {
    fn OnFrameReady(&mut self, _frame: &mut VideoFrame) {
        self.frames.fetch_add(1, Ordering::Relaxed);
    }

    fn Write(&mut self) {}
}

fn parse_arg<T: std::str::FromStr>(args: &[String], index: usize, default: T) -> T {
    if let Some(v) = args.get(index) {
        if let Ok(parsed) = v.parse::<T>() {
            return parsed;
        }
    }
    default
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 2 {
        eprintln!("Usage: rtmp_probe <rtmp_uri> [seconds=8] [width=1280] [height=720]");
        std::process::exit(2);
    }

    let uri = args[1].clone();
    let run_seconds: f64 = parse_arg(&args, 2, 8.0);
    let width: i32 = parse_arg(&args, 3, 1280);
    let height: i32 = parse_arg(&args, 4, 720);

    let frames = Arc::new(AtomicUsize::new(0));
    let client = ProbeVideoClient {
        width,
        height,
        format: PixelFormat::PIXEL_FORMAT_RGBA32,
        frames: frames.clone(),
    };

    let mut player = match Player::Create(uri.clone(), Box::new(client)) {
        Some(p) => p,
        None => {
            eprintln!("[FAIL] Player::Create failed for uri={}", uri);
            std::process::exit(1);
        }
    };

    player.Play();
    let start = Instant::now();
    let mut last_print = Instant::now();

    while start.elapsed().as_secs_f64() < run_seconds {
        player.Write();
        thread::sleep(Duration::from_millis(20));

        if last_print.elapsed().as_secs_f64() >= 1.0 {
            let count = frames.load(Ordering::Relaxed);
            println!(
                "[rtmp_probe] elapsed={:.2}s frames={}",
                start.elapsed().as_secs_f64(),
                count
            );
            last_print = Instant::now();
        }
    }

    let frame_count = frames.load(Ordering::Relaxed);
    println!(
        "[rtmp_probe] done uri={} seconds={:.2} frames={}",
        uri, run_seconds, frame_count
    );

    if frame_count == 0 {
        eprintln!("[FAIL] no frame received");
        std::process::exit(1);
    }
}
