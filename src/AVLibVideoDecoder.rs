#![allow(non_snake_case)]

use crate::AVLibUtil::PixelFormatToFFmpeg;
use crate::FixedSizeQueue::FixedSizeQueue;
use crate::IAVLibSource::IAVLibSource;
use crate::IVideoDescription::IVideoDescription;
use crate::PixelFormat::PixelFormat;
use crate::VideoFrame::VideoFrame;
use ffmpeg_next::software::scaling::{flag::Flags, Context as SwsContext};
use ffmpeg_next::util::error::EAGAIN;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Condvar, Mutex,
};
use std::thread;

pub struct AVLibVideoDecoder {
    _source: Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
    pub _parsedFrames: Arc<FixedSizeQueue<VideoFrame>>,
    _readyFrames: Arc<FixedSizeQueue<VideoFrame>>,
    _isRealtime: bool,
    _stayAlive: Arc<AtomicBool>,
    _thread: Option<thread::JoinHandle<()>>,
    _lastFrame: Arc<Mutex<Option<VideoFrame>>>,
    _seekRequest: Arc<AtomicBool>,
    _seekRequestTime: Arc<Mutex<f64>>,
    _continueMutex: Arc<Mutex<()>>,
    _continueCondition: Arc<Condvar>,
}

impl AVLibVideoDecoder {
    const DEFAULT_VIDEO_FRAME_QUEUE_SIZE: usize = 25;
    const COMPLETE_FRAMES_QUEUE_THRESHOLD: usize = Self::DEFAULT_VIDEO_FRAME_QUEUE_SIZE / 2;

    pub fn new(
        source: Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
        stream_idx: i32,
        target_desc: &dyn IVideoDescription,
        mut decoder: ffmpeg_next::decoder::Video,
        tb: f64,
    ) -> Self {
        let parsed = Arc::new(FixedSizeQueue::new(Self::DEFAULT_VIDEO_FRAME_QUEUE_SIZE));
        let ready = Arc::new(FixedSizeQueue::new(Self::DEFAULT_VIDEO_FRAME_QUEUE_SIZE));
        let stay_alive = Arc::new(AtomicBool::new(true));
        let seek_request = Arc::new(AtomicBool::new(true));
        let seek_request_time = Arc::new(Mutex::new(0.0));
        let last_frame = Arc::new(Mutex::new(None));
        let continue_mutex = Arc::new(Mutex::new(()));
        let continue_condition = Arc::new(Condvar::new());
        let target_width = target_desc.Width() as u32;
        let target_height = target_desc.Height() as u32;
        let target_format = target_desc.Format();
        let target_pixel = PixelFormatToFFmpeg(target_format);
        let is_realtime = if let Ok(s) = source.lock() {
            s.IsRealtime()
        } else {
            false
        };

        let mut obj = Self {
            _source: source.clone(),
            _parsedFrames: parsed.clone(),
            _readyFrames: ready.clone(),
            _isRealtime: is_realtime,
            _stayAlive: stay_alive.clone(),
            _thread: None,
            _lastFrame: last_frame.clone(),
            _seekRequest: seek_request.clone(),
            _seekRequestTime: seek_request_time.clone(),
            _continueMutex: continue_mutex.clone(),
            _continueCondition: continue_condition.clone(),
        };

        let t_source = source.clone();
        let t_stay_alive = stay_alive.clone();
        let t_parsed = parsed.clone();
        let t_ready_recycle = ready.clone();
        let t_last_frame = last_frame.clone();
        let t_seek_request = seek_request.clone();
        let t_seek_request_time = seek_request_time.clone();
        let t_target_width = target_width;
        let t_target_height = target_height;
        let t_continue_mutex = continue_mutex.clone();
        let t_continue_condition = continue_condition.clone();

        obj._thread = Some(thread::spawn(move || {
            let mut scaler: Option<SwsContext> = None;
            let mut scaler_source: Option<(ffmpeg_next::format::Pixel, u32, u32)> = None;

            let mut rgb_frame =
                ffmpeg_next::util::frame::Video::new(target_pixel, t_target_width, t_target_height);
            let w = t_target_width as usize;
            let h = t_target_height as usize;
            let mut decode_count: u64 = 0;
            let mut parse_count: u64 = 0;
            enum DrainStatus {
                NeedMoreInput,
                ReachedEof,
                Failed,
            }
            let copy_scaled_frame = |src: &ffmpeg_next::util::frame::Video,
                                     dst: &mut VideoFrame|
             -> bool {
                let plane_count = dst.BufferCount().max(0) as usize;
                if plane_count == 0 {
                    return false;
                }

                for plane in 0..plane_count {
                    let d_stride = dst.Stride(plane as i32);
                    if d_stride <= 0 {
                        return false;
                    }

                    let d_stride = d_stride as usize;
                    let s_stride = src.stride(plane);
                    if s_stride == 0 {
                        return false;
                    }

                    let rows = if target_format == PixelFormat::PIXEL_FORMAT_YUV420P && plane > 0 {
                        h / 2
                    } else {
                        h
                    };
                    let bytes_per_row = d_stride.min(s_stride);
                    let s_data = src.data(plane);
                    let Some(d_data) = dst.BufferMut(plane) else {
                        return false;
                    };

                    if rows == 0 || bytes_per_row == 0 {
                        continue;
                    }

                    for y in 0..rows {
                        let s_pos = y * s_stride;
                        let d_pos = y * d_stride;
                        if s_pos + bytes_per_row > s_data.len()
                            || d_pos + bytes_per_row > d_data.len()
                        {
                            return false;
                        }

                        d_data[d_pos..d_pos + bytes_per_row]
                            .copy_from_slice(&s_data[s_pos..s_pos + bytes_per_row]);
                    }
                }

                true
            };
            let push_eof_frame = || {
                let mut eof = t_ready_recycle
                    .TryPop()
                    .unwrap_or_else(|| VideoFrame::new(w as i32, h as i32, target_format));
                eof.SetAsEOF();
                t_parsed.Push(eof);
            };
            let mut drain_decoded_frames = |decoder: &mut ffmpeg_next::decoder::Video,
                                            scaler: &mut Option<SwsContext>,
                                            scaler_source: &mut Option<(
                ffmpeg_next::format::Pixel,
                u32,
                u32,
            )>,
                                            rgb_frame: &mut ffmpeg_next::util::frame::Video|
             -> DrainStatus {
                let mut decoded = ffmpeg_next::util::frame::Video::empty();
                loop {
                    match decoder.receive_frame(&mut decoded) {
                        Ok(()) => {
                            let decoded_fmt = decoded.format();
                            let decoded_w = decoded.width();
                            let decoded_h = decoded.height();

                            let needs_rebuild = match scaler_source {
                                Some((fmt, w, h)) => {
                                    *fmt != decoded_fmt || *w != decoded_w || *h != decoded_h
                                }
                                None => true,
                            };

                            if needs_rebuild {
                                match SwsContext::get(
                                    decoded_fmt,
                                    decoded_w,
                                    decoded_h,
                                    target_pixel,
                                    t_target_width,
                                    t_target_height,
                                    Flags::BILINEAR,
                                ) {
                                    Ok(new_scaler) => {
                                        *scaler = Some(new_scaler);
                                        *scaler_source = Some((decoded_fmt, decoded_w, decoded_h));
                                        crate::Logging::Debug::Debug::Log(&format!(
                                            "[AVLibVideoDecoder] stream={} rebuild_scaler {}x{} -> {}x{}",
                                            stream_idx,
                                            decoded_w,
                                            decoded_h,
                                            t_target_width,
                                            t_target_height
                                        ));
                                    }
                                    Err(_) => return DrainStatus::Failed,
                                }
                            }

                            let Some(scaler_ref) = scaler.as_mut() else {
                                return DrainStatus::Failed;
                            };

                            if scaler_ref.run(&decoded, rgb_frame).is_err() {
                                return DrainStatus::Failed;
                            }

                            let mut vf = t_ready_recycle.TryPop().unwrap_or_else(|| {
                                VideoFrame::new(w as i32, h as i32, target_format)
                            });
                            vf.SetTime(0.0);
                            vf.ClearEOF();
                            parse_count += 1;
                            if !copy_scaled_frame(rgb_frame, &mut vf) {
                                continue;
                            }

                            let ts = decoded.timestamp().unwrap_or(0) as f64;
                            vf.SetTime(ts * tb);
                            t_parsed.Push(vf);
                        }
                        Err(ffmpeg_next::Error::Eof) => return DrainStatus::ReachedEof,
                        Err(ffmpeg_next::Error::Other { errno }) if errno == EAGAIN => {
                            return DrainStatus::NeedMoreInput;
                        }
                        Err(_) => return DrainStatus::Failed,
                    }
                }
            };

            while t_stay_alive.load(Ordering::SeqCst) {
                if t_parsed.Full() {
                    let guard = t_continue_mutex
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let _ = t_continue_condition
                        .wait_timeout(guard, std::time::Duration::from_millis(5));
                    continue;
                }

                let packet_opt = if let Ok(mut s) = t_source.lock() {
                    s.TryGetNext(stream_idx)
                } else {
                    None
                };

                if let Some(p) = packet_opt {
                    if p.IsEOF() {
                        let _ = decoder.send_eof();
                        if matches!(
                            drain_decoded_frames(
                                &mut decoder,
                                &mut scaler,
                                &mut scaler_source,
                                &mut rgb_frame
                            ),
                            DrainStatus::ReachedEof
                        ) {
                            push_eof_frame();
                        }
                        if let Ok(mut s) = t_source.lock() {
                            s.Recycle(p);
                        }
                        continue;
                    }

                    if p.IsSeekRequest() {
                        t_parsed.Flush();
                        if let Ok(mut last_frame_lock) = t_last_frame.lock() {
                            *last_frame_lock = None;
                        }
                        if let Ok(mut seek_to) = t_seek_request_time.lock() {
                            *seek_to = p.SeekTime();
                        }
                        t_seek_request.store(false, Ordering::SeqCst);
                        decoder.flush();

                        if let Ok(mut s) = t_source.lock() {
                            s.Recycle(p);
                        }
                        continue;
                    }

                    let send_result = decoder.send_packet(&p.Packet);
                    if send_result.is_ok() {
                        decode_count += 1;
                        if decode_count % 120 == 0 {
                            crate::Logging::Debug::Debug::Log(&format!(
                                "[AVLibVideoDecoder] stream={} send_packet_ok={} queue_count={}",
                                stream_idx,
                                decode_count,
                                t_parsed.Count()
                            ));
                        }
                    } else {
                        crate::Logging::Debug::Debug::LogWarning(
                            "AVLibVideoDecoder::DecodeThread - send_packet failed",
                        );
                    }

                    match drain_decoded_frames(
                        &mut decoder,
                        &mut scaler,
                        &mut scaler_source,
                        &mut rgb_frame,
                    ) {
                        DrainStatus::ReachedEof => {
                            push_eof_frame();
                        }
                        DrainStatus::NeedMoreInput => {}
                        DrainStatus::Failed => {}
                    }

                    if let Ok(mut s) = t_source.lock() {
                        s.Recycle(p);
                    }
                } else {
                    let guard = t_continue_mutex
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let _ = t_continue_condition
                        .wait_timeout(guard, std::time::Duration::from_millis(5));
                }
            }
        }));

        obj
    }

    pub fn Recycle(&self, mut frame: VideoFrame) {
        frame.OnRecycle();
        self._readyFrames.Push(frame);
    }

    pub fn TryGetNext(&self, time: f64) -> Option<VideoFrame> {
        if self._parsedFrames.Count() <= Self::COMPLETE_FRAMES_QUEUE_THRESHOLD {
            self._continueCondition.notify_all();
        }

        let realtime_now = if let Ok(s) = self._source.lock() {
            s.IsRealtime()
        } else {
            self._isRealtime
        };

        if realtime_now {
            return self._parsedFrames.TryPop();
        }

        let seek_requested = !self._seekRequest.swap(true, Ordering::SeqCst);

        let mut last_frame = {
            let mut last = match self._lastFrame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };

            if last.is_none() || seek_requested {
                *last = self._parsedFrames.TryPop();
            }

            last.take()
        };

        if last_frame.is_none() {
            if seek_requested {
                // 与 C++ 一致：seek 后若还没拿到新帧，保持 seek 请求。
                self._seekRequest.store(false, Ordering::SeqCst);
            }
            return None;
        }

        let mut last_frame = last_frame.take().unwrap();
        let eof = last_frame.IsEOF();
        let mut behind = time >= last_frame.Time();

        if !eof && behind {
            let mut available = !self._parsedFrames.Empty();
            let mut next_frame: Option<VideoFrame> = None;
            let mut reached_eof = false;

            while behind && available && !reached_eof {
                next_frame = self._parsedFrames.TryPop();
                available = next_frame.is_some();

                if let Some(candidate) = next_frame.take() {
                    if candidate.IsEOF() {
                        self.Recycle(last_frame);
                        last_frame = candidate;
                        reached_eof = true;
                    } else {
                        behind = time >= candidate.Time();
                        if behind {
                            self.Recycle(last_frame);
                            last_frame = candidate;
                        } else {
                            next_frame = Some(candidate);
                        }
                    }
                }
            }

            if reached_eof {
                return Some(last_frame);
            }

            if !behind {
                let mut last = match self._lastFrame.lock() {
                    Ok(g) => g,
                    Err(poisoned) => poisoned.into_inner(),
                };
                *last = next_frame;
                return Some(last_frame);
            }

            return Some(last_frame);
        }

        {
            let mut last = match self._lastFrame.lock() {
                Ok(g) => g,
                Err(poisoned) => poisoned.into_inner(),
            };
            *last = Some(last_frame);
        }

        None
    }
}

impl Drop for AVLibVideoDecoder {
    fn drop(&mut self) {
        self._stayAlive.store(false, Ordering::SeqCst);
        self._continueCondition.notify_all();
        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }
    }
}
