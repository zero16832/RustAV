#![allow(non_snake_case)]

use crate::AVLibFileSource::AVLibFileSource;
use crate::AVLibRTMPSource::AVLibRTMPSource;
use crate::AVLibRTSPSource::AVLibRTSPSource;
use crate::AVLibVideoDecoder::AVLibVideoDecoder;
use crate::IAVLibDecoderVisitor::IAVLibDecoderVisitor;
use crate::IAVLibSource::IAVLibSource;
use crate::IVideoDescription::IVideoDescription;
use crate::VideoFrame::VideoFrame;
use ffmpeg_next::ffi::AVMediaType;
use std::any::Any;
use std::sync::{Arc, Mutex};

pub trait AVLibDecoderTrait {
    fn Accept(&self, visitor: &mut dyn crate::IAVLibDecoderVisitor::IAVLibDecoderVisitor);
    fn GetTimeBase(&self) -> f64;
    fn GetFrameRate(&self) -> f64;
    fn GetFrameDuration(&self) -> f64;
}

pub struct AVLibDecoder {
    _decoder: Arc<Mutex<AVLibVideoDecoder>>,
    pub _timeBase: f64,
    pub _frameRate: f64,
    pub _frameDuration: f64,
    pub _streamIndex: i32,
}

impl AVLibDecoder {
    fn CreateVideoDecoder(
        source: &dyn IAVLibSource,
        stream_index: i32,
    ) -> Option<ffmpeg_next::decoder::Video> {
        let any_source = source as &dyn Any;

        if let Some(file_source) = any_source.downcast_ref::<AVLibFileSource>() {
            return file_source.VideoDecoder(stream_index);
        }

        if let Some(rtsp_source) = any_source.downcast_ref::<AVLibRTSPSource>() {
            return rtsp_source.VideoDecoder(stream_index);
        }

        if let Some(rtmp_source) = any_source.downcast_ref::<AVLibRTMPSource>() {
            return rtmp_source.VideoDecoder(stream_index);
        }

        None
    }

    pub fn Create(
        source: Arc<Mutex<Box<dyn IAVLibSource + Send>>>,
        required_video_desc: &dyn IVideoDescription,
    ) -> Vec<Arc<AVLibDecoder>> {
        let stream_indices = if let Ok(s) = source.lock() {
            let mut found = Vec::new();
            let count = s.StreamCount();
            for i in 0..count {
                if s.StreamType(i) == AVMediaType::AVMEDIA_TYPE_VIDEO {
                    found.push(i);
                }
            }
            found
        } else {
            Vec::new()
        };

        let mut decoders = Vec::new();
        for stream_index in stream_indices {
            let (time_base, frame_rate, frame_duration, decoder_opt) = if let Ok(s) = source.lock()
            {
                (
                    s.TimeBase(stream_index),
                    s.FrameRate(stream_index),
                    s.FrameDuration(stream_index),
                    Self::CreateVideoDecoder(s.as_ref(), stream_index),
                )
            } else {
                continue;
            };

            let Some(video_decoder) = decoder_opt else {
                continue;
            };

            let decoder = Arc::new(Mutex::new(AVLibVideoDecoder::new(
                source.clone(),
                stream_index,
                required_video_desc,
                video_decoder,
                time_base,
            )));

            decoders.push(Arc::new(Self {
                _decoder: decoder,
                _timeBase: time_base,
                _frameRate: frame_rate,
                _frameDuration: frame_duration,
                _streamIndex: stream_index,
            }));
        }

        decoders
    }

    pub fn TryGetNext(&self, time: f64) -> Option<VideoFrame> {
        if let Ok(decoder) = self._decoder.lock() {
            decoder.TryGetNext(time)
        } else {
            None
        }
    }

    pub fn Recycle(&self, frame: VideoFrame) {
        if let Ok(decoder) = self._decoder.lock() {
            decoder.Recycle(frame);
        }
    }

    pub fn GetTimeBase(&self) -> f64 {
        self._timeBase
    }

    pub fn GetFrameRate(&self) -> f64 {
        self._frameRate
    }

    pub fn GetFrameDuration(&self) -> f64 {
        self._frameDuration
    }
}

impl AVLibDecoderTrait for AVLibDecoder {
    fn Accept(&self, visitor: &mut dyn IAVLibDecoderVisitor) {
        if let Ok(mut decoder) = self._decoder.lock() {
            visitor.Visit(&mut decoder);
        }
    }

    fn GetTimeBase(&self) -> f64 {
        self.GetTimeBase()
    }

    fn GetFrameRate(&self) -> f64 {
        self.GetFrameRate()
    }

    fn GetFrameDuration(&self) -> f64 {
        self.GetFrameDuration()
    }
}
