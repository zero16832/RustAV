#![allow(non_snake_case)]
#![allow(non_camel_case_types)]

use crate::FrameExportClient::{ExportedFrameMeta, SharedExportedFrameState};
use crate::Player::Player;
use lazy_static::lazy_static;
use std::ffi::CStr;
use std::os::raw::{c_char, c_double, c_int, c_void};
use std::slice;
use std::sync::{Arc, Mutex};

pub struct PlayerEntry {
    pub player: Arc<Mutex<Player>>,
    pub frame_export: Option<SharedExportedFrameState>,
}

#[repr(C)]
pub struct RustAVFrameMeta {
    pub width: i32,
    pub height: i32,
    pub format: i32,
    pub stride: i32,
    pub data_size: i32,
    pub time_sec: c_double,
    pub frame_index: i64,
}

lazy_static! {
    pub static ref gPlayers: Mutex<Vec<Option<Arc<PlayerEntry>>>> = Mutex::new(Vec::new());
}

fn NormalizePath(path: *const c_char) -> Option<String> {
    if path.is_null() {
        return None;
    }

    Some(
        unsafe { CStr::from_ptr(path) }
            .to_string_lossy()
            .into_owned(),
    )
}

fn StorePlayer(player: Option<Player>, frame_export: Option<SharedExportedFrameState>) -> c_int {
    let Some(player) = player else {
        return -1;
    };

    let mut players = match gPlayers.lock() {
        Ok(p) => p,
        Err(poisoned) => poisoned.into_inner(),
    };

    let entry = Arc::new(PlayerEntry {
        player: Arc::new(Mutex::new(player)),
        frame_export,
    });
    players.push(Some(entry));
    (players.len() - 1) as c_int
}

#[no_mangle]
pub extern "system" fn GetPlayer(path: *const c_char, targetTexture: *mut c_void) -> c_int {
    if path.is_null() || targetTexture.is_null() {
        return -1;
    }

    let Some(path_str) = NormalizePath(path) else {
        return -1;
    };

    TryCleanPlayersCache();
    StorePlayer(Player::CreateWithTexture(path_str, targetTexture), None)
}

#[no_mangle]
pub extern "system" fn CreatePlayerPullRGBA(
    path: *const c_char,
    targetWidth: c_int,
    targetHeight: c_int,
) -> c_int {
    let Some(path_str) = NormalizePath(path) else {
        return -1;
    };

    if targetWidth <= 0 || targetHeight <= 0 {
        return -1;
    }

    TryCleanPlayersCache();

    let created = Player::CreateWithFrameExport(path_str, targetWidth, targetHeight);
    match created {
        Some((player, shared)) => StorePlayer(Some(player), Some(shared)),
        None => -1,
    }
}

pub fn ForcePlayersWrite() {
    let players = match gPlayers.lock() {
        Ok(p) => p,
        Err(poisoned) => poisoned.into_inner(),
    };

    let snapshot: Vec<Arc<Mutex<Player>>> = players
        .iter()
        .filter_map(|p| p.as_ref().map(|entry| entry.player.clone()))
        .collect();
    drop(players);

    for player in snapshot {
        match player.lock() {
            Ok(mut guard) => guard.Write(),
            Err(poisoned) => poisoned.into_inner().Write(),
        }
    }
}

#[no_mangle]
pub extern "system" fn ReleasePlayer(id: c_int) -> c_int {
    if id < 0 {
        return -1;
    }

    let released = {
        let mut players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
        let idx = id as usize;
        if idx >= players.len() {
            return -1;
        }

        players[idx].take()
    };

    if released.is_some() { 1 } else { -1 }
}

#[no_mangle]
pub extern "system" fn ForcePlayerWrite(id: c_int) {
    with_player_mut(id, (), |player| {
        player.Write();
    });
}

#[no_mangle]
pub extern "system" fn UpdatePlayer(id: c_int) -> c_int {
    with_player_mut(id, -1, |player| {
        player.Write();
        0
    })
}

#[no_mangle]
pub extern "system" fn Duration(id: c_int) -> c_double {
    with_player(id, -1.0, |player| player.Duration())
}

#[no_mangle]
pub extern "system" fn Time(id: c_int) -> c_double {
    with_player(id, -1.0, |player| player.CurrentTime())
}

#[no_mangle]
pub extern "system" fn Play(id: c_int) -> c_int {
    with_player(id, -1, |player| {
        player.Play();
        0
    })
}

#[no_mangle]
pub extern "system" fn Stop(id: c_int) -> c_int {
    with_player(id, -1, |player| {
        player.Stop();
        0
    })
}

#[no_mangle]
pub extern "system" fn Seek(id: c_int, time: c_double) -> c_double {
    with_player(id, -1.0, |player| {
        player.Seek(time);
        0.0
    })
}

#[no_mangle]
pub extern "system" fn SetLoop(id: c_int, loop_value: bool) -> c_int {
    with_player(id, -1, |player| {
        player.SetLoop(loop_value);
        0
    })
}

#[no_mangle]
pub extern "system" fn GetFrameMetaRGBA(id: c_int, outMeta: *mut RustAVFrameMeta) -> c_int {
    if outMeta.is_null() {
        return -1;
    }

    let Some(shared) = snapshot_frame_export(id) else {
        return -1;
    };

    let shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let meta = shared.Meta();
    unsafe {
        *outMeta = ToCFrameMeta(meta);
    }

    if meta.HasFrame { 1 } else { 0 }
}

#[no_mangle]
pub extern "system" fn CopyFrameRGBA(
    id: c_int,
    destination: *mut u8,
    destinationLength: c_int,
) -> c_int {
    if destination.is_null() || destinationLength <= 0 {
        return -1;
    }

    let Some(shared) = snapshot_frame_export(id) else {
        return -1;
    };

    let shared = shared.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let destination = unsafe { slice::from_raw_parts_mut(destination, destinationLength as usize) };
    let copied = shared.CopyTo(destination);

    if copied == 0 && shared.Meta().HasFrame {
        -1
    } else {
        copied
    }
}

fn ToCFrameMeta(meta: ExportedFrameMeta) -> RustAVFrameMeta {
    RustAVFrameMeta {
        width: meta.Width,
        height: meta.Height,
        format: meta.Format as i32,
        stride: meta.Stride,
        data_size: meta.DataLength,
        time_sec: meta.Time,
        frame_index: meta.FrameIndex,
    }
}

pub fn ValidatePlayerId(id: c_int) -> bool {
    if id < 0 {
        return false;
    }

    let players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
    if id as usize >= players.len() {
        return false;
    }

    players[id as usize].is_some()
}

fn with_player<T, F>(id: c_int, default: T, f: F) -> T
where
    F: FnOnce(&Player) -> T,
{
    let player = match snapshot_player(id) {
        Some(p) => p,
        None => return default,
    };

    let result = match player.lock() {
        Ok(guard) => f(&guard),
        Err(poisoned) => {
            let guard = poisoned.into_inner();
            f(&guard)
        }
    };

    result
}

fn with_player_mut<T, F>(id: c_int, default: T, f: F) -> T
where
    F: FnOnce(&mut Player) -> T,
{
    let player = match snapshot_player(id) {
        Some(p) => p,
        None => return default,
    };

    let result = match player.lock() {
        Ok(mut guard) => f(&mut guard),
        Err(poisoned) => {
            let mut guard = poisoned.into_inner();
            f(&mut guard)
        }
    };

    result
}

fn snapshot_entry(id: c_int) -> Option<Arc<PlayerEntry>> {
    if id < 0 {
        return None;
    }

    let players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());
    let idx = id as usize;
    if idx >= players.len() {
        return None;
    }

    players[idx].as_ref().cloned()
}

fn snapshot_player(id: c_int) -> Option<Arc<Mutex<Player>>> {
    snapshot_entry(id).map(|entry| entry.player.clone())
}

fn snapshot_frame_export(id: c_int) -> Option<SharedExportedFrameState> {
    snapshot_entry(id).and_then(|entry| entry.frame_export.clone())
}

pub fn TryCleanPlayersCache() {
    let mut players = gPlayers.lock().unwrap_or_else(|p| p.into_inner());

    if players.is_empty() {
        return;
    }

    let any_enabled = players.iter().any(|p| p.is_some());
    if !any_enabled {
        players.clear();
    }
}
