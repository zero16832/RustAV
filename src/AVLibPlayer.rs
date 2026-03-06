#![allow(non_snake_case)]

use crate::AVLibDecoder::AVLibDecoder;
use crate::AVLibFileSource::AVLibFileSource;
use crate::AVLibRTMPSource::AVLibRTMPSource;
use crate::AVLibRTSPSource::AVLibRTSPSource;
use crate::IAVLibSource::IAVLibSource;
use crate::IFrameVisitor::IFrameVisitor;
use crate::IVideoClient::IVideoClient;
use crate::PixelFormat::PixelFormat;
use crate::VideoFrame::VideoFrame;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Condvar, Mutex, Once,
};
use std::thread;
use std::time::{Duration, Instant};

static PROCESS_WIDE_INIT: Once = Once::new();

pub struct AVLibPlayer {
    pub _source: Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
    pub _decoders: Arc<Mutex<Vec<Arc<AVLibDecoder>>>>,
    _video_client: Arc<Mutex<Box<dyn IVideoClient + Send>>>,
    _time: Arc<Mutex<i64>>,
    _playing: Arc<AtomicBool>,
    _looping: Arc<AtomicBool>,
    _stayAlive: Arc<AtomicBool>,
    _lastTick: Arc<Mutex<Instant>>,
    _killMutex: Arc<Mutex<()>>,
    _killCondition: Arc<Condvar>,
    _thread: Option<thread::JoinHandle<()>>,
}

impl AVLibPlayer {
    const RTSP_PREFIX: &'static str = "rtsp://";
    const RTMP_PREFIX: &'static str = "rtmp://";
    const CONNECT_RETRY_MILLISECONDS: u64 = 200;
    const REALTIME_POLL_MILLISECONDS: u64 = 5;

    fn ProcessWideInitialize() {
        PROCESS_WIDE_INIT.call_once(|| {
            let _ = ffmpeg_next::init();
            unsafe {
                ffmpeg_next::ffi::av_log_set_level(ffmpeg_next::ffi::AV_LOG_VERBOSE);
            }
        });
    }

    fn SleepTimeFromDecoders(decoders: &[Arc<AVLibDecoder>]) -> Duration {
        let mut min_frame_duration = f64::MAX;
        for decoder in decoders.iter() {
            let d = decoder.GetFrameDuration();
            if d > 0.0 && d < min_frame_duration {
                min_frame_duration = d;
            }
        }

        if min_frame_duration.is_finite() && min_frame_duration > 0.0 {
            Duration::from_secs_f64((min_frame_duration * 0.5).max(0.001))
        } else {
            Duration::from_micros(50_000)
        }
    }

    fn RealtimeSleepTimeFromDecoders(decoders: &[Arc<AVLibDecoder>]) -> Duration {
        let mut min_frame_duration = f64::MAX;
        for decoder in decoders.iter() {
            let d = decoder.GetFrameDuration();
            if d > 0.0 && d < min_frame_duration {
                min_frame_duration = d;
            }
        }

        if min_frame_duration.is_finite() && min_frame_duration > 0.0 {
            Duration::from_secs_f64((min_frame_duration * 0.25).clamp(0.001, 0.005))
        } else {
            Duration::from_millis(Self::REALTIME_POLL_MILLISECONDS)
        }
    }

    fn EnsureConnection(
        source: &Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
        stay_alive: &Arc<AtomicBool>,
        kill_mutex: &Arc<Mutex<()>>,
        kill_condition: &Arc<Condvar>,
    ) -> bool {
        while stay_alive.load(Ordering::SeqCst) {
            let connected = if let Ok(mut s) = source.lock() {
                if !s.IsConnected() {
                    s.Connect();
                }
                s.IsConnected()
            } else {
                false
            };

            if connected {
                return true;
            }

            if !Self::WaitOrInterrupted(
                stay_alive,
                kill_mutex,
                kill_condition,
                Duration::from_millis(Self::CONNECT_RETRY_MILLISECONDS),
            ) {
                return false;
            }
        }

        false
    }

    fn WaitOrInterrupted(
        stay_alive: &Arc<AtomicBool>,
        kill_mutex: &Arc<Mutex<()>>,
        kill_condition: &Arc<Condvar>,
        timeout: Duration,
    ) -> bool {
        if !stay_alive.load(Ordering::SeqCst) {
            return false;
        }

        let guard = match kill_mutex.lock() {
            Ok(g) => g,
            Err(poisoned) => poisoned.into_inner(),
        };
        let _ = kill_condition.wait_timeout(guard, timeout);

        stay_alive.load(Ordering::SeqCst)
    }

    pub fn new(
        uri: String,
        target_width: i32,
        target_height: i32,
        target_format: PixelFormat,
        video_client: Box<dyn IVideoClient + Send>,
    ) -> Option<Self> {
        Self::ProcessWideInitialize();

        let source_box: Box<dyn IAVLibSource + Send> = if uri.contains(Self::RTSP_PREFIX) {
            Box::new(AVLibRTSPSource::new(uri.clone()))
        } else if uri.contains(Self::RTMP_PREFIX) {
            Box::new(AVLibRTMPSource::new(uri.clone()))
        } else {
            Box::new(AVLibFileSource::new(uri.clone()))
        };

        let source = Arc::new(Mutex::new(source_box));
        if let Ok(mut s) = source.lock() {
            s.Connect();
        }

        let mut player = Self {
            _source: source.clone(),
            _decoders: Arc::new(Mutex::new(Vec::new())),
            _video_client: Arc::new(Mutex::new(video_client)),
            _time: Arc::new(Mutex::new(0)),
            _playing: Arc::new(AtomicBool::new(false)),
            _looping: Arc::new(AtomicBool::new(false)),
            _stayAlive: Arc::new(AtomicBool::new(true)),
            _lastTick: Arc::new(Mutex::new(Instant::now())),
            _killMutex: Arc::new(Mutex::new(())),
            _killCondition: Arc::new(Condvar::new()),
            _thread: None,
        };

        struct TargetVideoDescription {
            width: i32,
            height: i32,
            format: PixelFormat,
        }

        impl crate::IVideoDescription::IVideoDescription for TargetVideoDescription {
            fn Width(&self) -> i32 {
                self.width
            }
            fn Height(&self) -> i32 {
                self.height
            }
            fn Format(&self) -> crate::PixelFormat::PixelFormat {
                self.format
            }
        }

        let target_desc = TargetVideoDescription {
            width: target_width,
            height: target_height,
            format: target_format,
        };

        let t_source = player._source.clone();
        let t_stay_alive = player._stayAlive.clone();
        let t_playing = player._playing.clone();
        let t_time = player._time.clone();
        let t_last_tick = player._lastTick.clone();
        let t_decoders = player._decoders.clone();
        let t_looping = player._looping.clone();
        let t_video_client = player._video_client.clone();
        let t_kill_mutex = player._killMutex.clone();
        let t_kill_condition = player._killCondition.clone();
        let t_target_width = target_desc.width;
        let t_target_height = target_desc.height;
        let t_target_format = target_desc.format;

        player._thread = Some(thread::spawn(move || {
            struct TargetVideoDescription {
                width: i32,
                height: i32,
                format: PixelFormat,
            }

            impl crate::IVideoDescription::IVideoDescription for TargetVideoDescription {
                fn Width(&self) -> i32 {
                    self.width
                }
                fn Height(&self) -> i32 {
                    self.height
                }
                fn Format(&self) -> crate::PixelFormat::PixelFormat {
                    self.format
                }
            }

            let target_desc = TargetVideoDescription {
                width: t_target_width,
                height: t_target_height,
                format: t_target_format,
            };

            let mut sleep_duration = Duration::from_millis(Self::REALTIME_POLL_MILLISECONDS);

            while t_stay_alive.load(Ordering::SeqCst) {
                let connected = Self::EnsureConnection(
                    &t_source,
                    &t_stay_alive,
                    &t_kill_mutex,
                    &t_kill_condition,
                );
                if !connected {
                    break;
                }

                let mut decoders_snapshot = {
                    if let Ok(decoders) = t_decoders.lock() {
                        decoders.clone()
                    } else {
                        Vec::new()
                    }
                };

                if decoders_snapshot.is_empty() {
                    let created = AVLibDecoder::Create(t_source.clone(), &target_desc);
                    if created.is_empty() {
                        if !Self::WaitOrInterrupted(
                            &t_stay_alive,
                            &t_kill_mutex,
                            &t_kill_condition,
                            Duration::from_millis(Self::CONNECT_RETRY_MILLISECONDS),
                        ) {
                            break;
                        }
                        continue;
                    }

                    if let Ok(mut decoders) = t_decoders.lock() {
                        *decoders = created;
                        decoders_snapshot = decoders.clone();
                    } else {
                        decoders_snapshot = created;
                    }

                    let is_realtime = if let Ok(s) = t_source.lock() {
                        s.IsRealtime()
                    } else {
                        false
                    };
                    sleep_duration = if is_realtime {
                        Self::RealtimeSleepTimeFromDecoders(&decoders_snapshot)
                    } else {
                        Self::SleepTimeFromDecoders(&decoders_snapshot)
                    };
                }

                if t_playing.load(Ordering::SeqCst) {
                    let now = Instant::now();
                    let current_time = {
                        let mut last = t_last_tick.lock().unwrap_or_else(|p| p.into_inner());
                        let d = now.duration_since(*last).as_micros() as i64;
                        *last = now;

                        let mut clock = t_time.lock().unwrap_or_else(|p| p.into_inner());
                        *clock += d;
                        *clock as f64 / 1_000_000.0
                    };

                    let mut eof_hit = false;
                    for decoder in decoders_snapshot.iter() {
                        if let Some(mut frame) = decoder.TryGetNext(current_time) {
                            if frame.IsEOF() {
                                eof_hit = true;
                            } else {
                                struct VideoClientVisitor<'a> {
                                    client: &'a mut dyn IVideoClient,
                                }

                                impl<'a> IFrameVisitor for VideoClientVisitor<'a> {
                                    fn Visit(&mut self, frame: &mut VideoFrame) {
                                        self.client.OnFrameReady(frame);
                                    }
                                }

                                match t_video_client.lock() {
                                    Ok(mut client) => {
                                        let client_ref: &mut dyn IVideoClient = client.as_mut();
                                        let mut visitor = VideoClientVisitor { client: client_ref };
                                        frame.Accept(&mut visitor);
                                    }
                                    Err(poisoned) => {
                                        let mut client = poisoned.into_inner();
                                        let client_ref: &mut dyn IVideoClient = client.as_mut();
                                        let mut visitor = VideoClientVisitor { client: client_ref };
                                        frame.Accept(&mut visitor);
                                    }
                                }
                                decoder.Recycle(frame);
                            }
                        }
                    }

                    if eof_hit {
                        if t_looping.load(Ordering::SeqCst) {
                            let from = {
                                if let Ok(clock) = t_time.lock() {
                                    *clock as f64 / 1_000_000.0
                                } else {
                                    0.0
                                }
                            };

                            if let Ok(mut s) = t_source.lock() {
                                s.Seek(from, 0.0);
                            }

                            if let Ok(mut clock) = t_time.lock() {
                                *clock = 0;
                            }
                        } else {
                            t_playing.store(false, Ordering::SeqCst);
                        }
                    }
                } else if let Ok(mut last) = t_last_tick.lock() {
                    *last = Instant::now();
                }

                if !Self::WaitOrInterrupted(
                    &t_stay_alive,
                    &t_kill_mutex,
                    &t_kill_condition,
                    sleep_duration,
                ) {
                    break;
                }
            }
        }));

        Some(player)
    }

    pub fn Write(&self) {
        match self._video_client.lock() {
            Ok(mut client) => client.Write(),
            Err(poisoned) => poisoned.into_inner().Write(),
        }
    }

    pub fn Play(&self) {
        let was_playing = self._playing.swap(true, Ordering::SeqCst);
        if !was_playing {
            if self.IsRealtime() {
                let decoders = if let Ok(decoders) = self._decoders.lock() {
                    decoders.clone()
                } else {
                    Vec::new()
                };

                for decoder in decoders.iter() {
                    decoder.FlushRealtimeFrames();
                }
            }

            if let Ok(mut last) = self._lastTick.lock() {
                *last = Instant::now();
            }
        }
    }

    pub fn Stop(&self) {
        self._playing.store(false, Ordering::SeqCst);
    }

    pub fn CanSeek(&self) -> bool {
        if let Ok(s) = self._source.lock() {
            s.CanSeek()
        } else {
            false
        }
    }

    pub fn Seek(&self, mut to: f64) {
        if !self._playing.load(Ordering::SeqCst) || !self.CanSeek() {
            return;
        }

        let duration = self.Duration();
        if to > duration {
            to = duration;
        } else if to < 0.0 {
            to = 0.0;
        }

        let from = self.CurrentTime();
        if let Ok(mut s) = self._source.lock() {
            s.Seek(from, to);
        }

        if let Ok(mut clock) = self._time.lock() {
            *clock = (to * 1_000_000.0) as i64;
        }
    }

    pub fn CanLoop(&self) -> bool {
        self.CanSeek()
    }

    pub fn SetLoop(&self, loop_value: bool) {
        if !self.CanLoop() {
            return;
        }
        self._looping.store(loop_value, Ordering::SeqCst);
    }

    pub fn IsLooping(&self) -> bool {
        self._looping.load(Ordering::SeqCst)
    }

    pub fn Duration(&self) -> f64 {
        if let Ok(s) = self._source.lock() {
            s.Duration()
        } else {
            -1.0
        }
    }

    pub fn CurrentTime(&self) -> f64 {
        if let Ok(clock) = self._time.lock() {
            *clock as f64 / 1_000_000.0
        } else {
            0.0
        }
    }

    pub fn IsPlaying(&self) -> bool {
        self._playing.load(Ordering::SeqCst)
    }

    pub fn IsRealtime(&self) -> bool {
        if let Ok(s) = self._source.lock() {
            s.IsRealtime()
        } else {
            false
        }
    }
}

impl Drop for AVLibPlayer {
    fn drop(&mut self) {
        self._stayAlive.store(false, Ordering::SeqCst);
        self._killCondition.notify_all();

        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }

        if let Ok(mut decoders) = self._decoders.lock() {
            decoders.clear();
        }
    }
}
