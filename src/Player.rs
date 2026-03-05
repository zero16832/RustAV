#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

use crate::AVLibPlayer::AVLibPlayer;
use crate::IVideoClient::IVideoClient;
use crate::Logging::Debug::Debug;
use crate::PixelFormat::PixelFormat;
use crate::TextureClient::TextureClient;
use std::os::raw::c_void;

pub struct Player {
    pub _avlib_player: AVLibPlayer,
    _uri: String,
}

impl Player {
    pub const RTSP_PREFIX: &'static str = "rtsp://";
    pub const RTMP_PREFIX: &'static str = "rtmp://";

    pub fn Create(uri: String, video_client: Box<dyn IVideoClient + Send>) -> Option<Self> {
        if !uri.contains(Self::RTSP_PREFIX) && !uri.contains(Self::RTMP_PREFIX) {
            if std::fs::File::open(&uri).is_err() {
                Debug::LogError(&format!("File does not exist at given uri: {}", uri));
                return None;
            }
        }

        let target_width = video_client.Width();
        let target_height = video_client.Height();
        let target_format = match video_client.Format() {
            PixelFormat::PIXEL_FORMAT_NONE => PixelFormat::PIXEL_FORMAT_RGBA32,
            f => f,
        };

        let av_player = AVLibPlayer::new(
            uri.clone(),
            target_width,
            target_height,
            target_format,
            video_client,
        )?;

        Some(Self {
            _avlib_player: av_player,
            _uri: uri,
        })
    }

    pub fn CreateWithTexture(uri: String, target_texture: *mut c_void) -> Option<Self> {
        let client = TextureClient::new(target_texture)?;
        Self::Create(uri, Box::new(client))
    }

    pub fn Write(&mut self) {
        self._avlib_player.Write();
    }

    pub fn Play(&self) {
        self._avlib_player.Play();
    }

    pub fn Stop(&self) {
        self._avlib_player.Stop();
    }

    pub fn CanSeek(&self) -> bool {
        self._avlib_player.CanSeek()
    }

    pub fn Seek(&self, time: f64) {
        self._avlib_player.Seek(time);
    }

    pub fn CanLoop(&self) -> bool {
        self._avlib_player.CanLoop()
    }

    pub fn SetLoop(&self, loop_value: bool) {
        self._avlib_player.SetLoop(loop_value);
    }

    pub fn IsLooping(&self) -> bool {
        self._avlib_player.IsLooping()
    }

    pub fn IsPlaying(&self) -> bool {
        self._avlib_player.IsPlaying()
    }

    pub fn IsRealtime(&self) -> bool {
        self._avlib_player.IsRealtime()
    }

    pub fn Duration(&self) -> f64 {
        self._avlib_player.Duration()
    }

    pub fn CurrentTime(&self) -> f64 {
        self._avlib_player.CurrentTime()
    }
}
