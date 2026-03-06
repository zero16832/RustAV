#![allow(non_snake_case)]

use crate::IVideoClient::IVideoClient;
use crate::IVideoDescription::IVideoDescription;
use crate::PixelFormat::PixelFormat;
use crate::Rendering::NullTextureWriter::NullTextureWriter;
use crate::Rendering::TextureWriter::{TextureWriter, TextureWriterLike};
use crate::VideoFrame::VideoFrame;
use std::os::raw::c_void;

#[cfg(windows)]
use crate::Rendering::D3D11TextureWriter::D3D11TextureWriter;

pub struct TextureClient {
    _writer: Box<dyn TextureWriterLike>,
}

impl TextureClient {
    pub fn new(target_texture: *mut c_void) -> Option<Self> {
        let writer = TextureWriter::Create(target_texture)?;
        Some(Self { _writer: writer })
    }

    pub fn from_null_writer(width: i32, height: i32) -> Self {
        Self {
            _writer: Box::new(NullTextureWriter::new(width, height)),
        }
    }

    #[cfg(windows)]
    pub fn from_d3d11_writer(writer: D3D11TextureWriter) -> Self {
        Self {
            _writer: Box::new(writer),
        }
    }
}

impl IVideoDescription for TextureClient {
    fn Width(&self) -> i32 {
        self._writer.Width()
    }

    fn Height(&self) -> i32 {
        self._writer.Height()
    }

    fn Format(&self) -> PixelFormat {
        self._writer.Format()
    }
}

impl IVideoClient for TextureClient {
    fn OnFrameReady(&mut self, frame: &mut VideoFrame) {
        let p0 = frame.Buffer(0).unwrap_or(&[]);
        let p1 = frame.Buffer(1).unwrap_or(&[]);
        let p2 = frame.Buffer(2).unwrap_or(&[]);
        let p3 = frame.Buffer(3).unwrap_or(&[]);
        let all = [p0, p1, p2, p3];
        let count = frame.BufferCount().clamp(0, all.len() as i32) as usize;
        self._writer.ReadPlanes(&all[..count]);
    }

    fn Write(&mut self) {
        self._writer.Write(false);
    }
}
