#![allow(non_snake_case)]
use crate::dllmain::{UnityInterfacesReady, UnityRenderer};
use crate::PixelFormat::PixelFormat;
use std::os::raw::c_void;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

#[cfg(windows)]
use crate::Rendering::D3D11TextureWriter::D3D11TextureWriter;

#[cfg(windows)]
const K_UNITY_GFX_RENDERER_D3D11: i32 = 2;

pub trait TextureWriterLike: Send {
    fn Format(&self) -> PixelFormat;
    fn Width(&self) -> i32;
    fn Height(&self) -> i32;
    fn ReadPlanes(&mut self, source: &[&[u8]]);
    fn Write(&mut self, force: bool);
}

pub struct TextureWriter {
    pub(crate) Ready: AtomicBool,
    pub(crate) Changed: AtomicBool,
    pub(crate) BufferMutex: Mutex<()>,
    pub(crate) TargetWidth: i32,
    pub(crate) TargetHeight: i32,
    pub(crate) TargetFormat: PixelFormat,
    pub(crate) BufferSizes: Vec<i32>,
    pub(crate) BufferStrides: Vec<i32>,
    pub(crate) Buffers: Vec<Vec<u8>>,
}

impl TextureWriter {
    pub fn Create(target_texture: *mut c_void) -> Option<Box<dyn TextureWriterLike>> {
        if target_texture.is_null() {
            return None;
        }

        if !UnityInterfacesReady() {
            return None;
        }

        #[cfg(windows)]
        match UnityRenderer() {
            K_UNITY_GFX_RENDERER_D3D11 => unsafe {
                D3D11TextureWriter::new(target_texture)
                    .map(|w| Box::new(w) as Box<dyn TextureWriterLike>)
            },
            _ => None,
        }

        #[cfg(not(windows))]
        {
            let _ = target_texture;
            None
        }
    }

    pub fn new() -> Self {
        Self {
            Ready: AtomicBool::new(false),
            Changed: AtomicBool::new(false),
            BufferMutex: Mutex::new(()),
            TargetWidth: 0,
            TargetHeight: 0,
            TargetFormat: PixelFormat::PIXEL_FORMAT_NONE,
            BufferSizes: Vec::new(),
            BufferStrides: Vec::new(),
            Buffers: Vec::new(),
        }
    }

    pub fn Read(&mut self, source: &[u8]) {
        self.ReadPlanes(&[source]);
    }

    pub fn ReadPlanes(&mut self, source: &[&[u8]]) {
        if !self.Ready.load(Ordering::SeqCst) {
            return;
        }

        if self.Buffers.is_empty() || source.len() < self.Buffers.len() {
            return;
        }

        for i in 0..self.Buffers.len() {
            let target_len = if i < self.BufferSizes.len() && self.BufferSizes[i] > 0 {
                self.BufferSizes[i] as usize
            } else {
                self.Buffers[i].len()
            };
            if source[i].len() < target_len || self.Buffers[i].len() < target_len {
                return;
            }
        }

        let _lock = self.BufferMutex.lock().unwrap();
        for i in 0..self.Buffers.len() {
            let target_len = if i < self.BufferSizes.len() && self.BufferSizes[i] > 0 {
                self.BufferSizes[i] as usize
            } else {
                self.Buffers[i].len()
            };
            self.Buffers[i][..target_len].copy_from_slice(&source[i][..target_len]);
        }
        self.Changed.store(true, Ordering::SeqCst);
    }

    pub fn Format(&self) -> PixelFormat {
        self.TargetFormat
    }

    pub fn Width(&self) -> i32 {
        self.TargetWidth
    }

    pub fn Height(&self) -> i32 {
        self.TargetHeight
    }

    pub fn BufferCount(&self) -> i32 {
        self.Buffers.len() as i32
    }

    pub fn BufferSize(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self.BufferSizes.len() {
            return -1;
        }

        self.BufferSizes[index as usize]
    }

    pub fn BufferStride(&self, index: i32) -> i32 {
        if index < 0 || index as usize >= self.BufferStrides.len() {
            return -1;
        }

        self.BufferStrides[index as usize]
    }
}
