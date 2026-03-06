#![allow(non_snake_case)]

use crate::IVideoClient::IVideoClient;
use crate::IVideoDescription::IVideoDescription;
use crate::PixelFormat::PixelFormat;
use crate::VideoFrame::VideoFrame;
use std::sync::{Arc, Mutex};

#[derive(Clone, Copy)]
pub struct ExportedFrameMeta {
    pub Width: i32,
    pub Height: i32,
    pub Stride: i32,
    pub DataLength: i32,
    pub Format: PixelFormat,
    pub Time: f64,
    pub FrameIndex: i64,
    pub HasFrame: bool,
}

pub struct ExportedFrameState {
    width: i32,
    height: i32,
    stride: i32,
    format: PixelFormat,
    time: f64,
    frame_index: i64,
    has_frame: bool,
    data: Vec<u8>,
}

pub type SharedExportedFrameState = Arc<Mutex<ExportedFrameState>>;

impl ExportedFrameState {
    pub fn new(width: i32, height: i32, format: PixelFormat) -> Self {
        let stride = if format == PixelFormat::PIXEL_FORMAT_RGBA32 {
            width.saturating_mul(4)
        } else {
            0
        };
        let data_length = stride.saturating_mul(height).max(0) as usize;

        Self {
            width,
            height,
            stride,
            format,
            time: 0.0,
            frame_index: 0,
            has_frame: false,
            data: vec![0; data_length],
        }
    }

    pub fn Meta(&self) -> ExportedFrameMeta {
        ExportedFrameMeta {
            Width: self.width,
            Height: self.height,
            Stride: self.stride,
            DataLength: self.data.len() as i32,
            Format: self.format,
            Time: self.time,
            FrameIndex: self.frame_index,
            HasFrame: self.has_frame,
        }
    }

    pub fn CopyTo(&self, destination: &mut [u8]) -> i32 {
        if !self.has_frame || destination.len() < self.data.len() {
            return 0;
        }

        destination[..self.data.len()].copy_from_slice(&self.data);
        self.data.len() as i32
    }
}

pub struct FrameExportClient {
    width: i32,
    height: i32,
    format: PixelFormat,
    shared: SharedExportedFrameState,
}

impl FrameExportClient {
    pub fn new(
        width: i32,
        height: i32,
        format: PixelFormat,
    ) -> (Self, SharedExportedFrameState) {
        let shared = Arc::new(Mutex::new(ExportedFrameState::new(width, height, format)));
        (
            Self {
                width,
                height,
                format,
                shared: shared.clone(),
            },
            shared,
        )
    }
}

impl IVideoDescription for FrameExportClient {
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

impl IVideoClient for FrameExportClient {
    fn OnFrameReady(&mut self, frame: &mut VideoFrame) {
        if self.format != PixelFormat::PIXEL_FORMAT_RGBA32 {
            return;
        }

        let Some(source) = frame.Buffer(0) else {
            return;
        };

        let mut shared = self.shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        if shared.data.len() != source.len() {
            shared.data.resize(source.len(), 0);
        }

        shared.data.copy_from_slice(source);
        shared.time = frame.Time();
        shared.frame_index = shared.frame_index.saturating_add(1);
        shared.has_frame = true;
    }

    fn Write(&mut self) {}
}
