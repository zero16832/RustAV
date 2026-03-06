# Unity 插件 ABI 说明

## 目标

RustAV 当前面向 Unity 插件提供两条视频输出路径：

1. Windows D3D11 纹理写入。
2. 通用 CPU RGBA 拉帧导出（Windows / iOS / Android 统一）。

推荐策略：

1. Windows 如已持有 Unity D3D11 纹理，优先使用纹理写入。
2. iOS / Android 统一使用 RGBA 拉帧接口。

## 导出函数

### 播放器创建

1. `GetPlayer(path, targetTexture) -> int`
   - 仅适用于 Windows D3D11 纹理路径。
   - 成功返回 `player id`。
   - 失败返回 `-1`。
2. `CreatePlayerPullRGBA(path, targetWidth, targetHeight) -> int`
   - 适用于 Windows / iOS / Android。
   - `targetWidth`、`targetHeight` 必须大于 `0`。
   - 成功返回 `player id`。
   - 失败返回 `-1`。

### 生命周期控制

1. `Play(id) -> int`
   - 启动拉流和解码节奏。
   - 成功返回 `0`，失败返回 `-1`。
2. `Stop(id) -> int`
   - 停止播放。
   - 成功返回 `0`，失败返回 `-1`。
3. `ReleasePlayer(id) -> int`
   - 释放播放器。
   - 成功返回 `1`，失败返回 `-1`。

### 运行期辅助

1. `UpdatePlayer(id) -> int`
   - Windows 纹理模式：用于执行纹理写入。
   - RGBA 拉帧模式：可安全调用，但当前不会产生额外写入动作。
2. `Duration(id) -> double`
3. `Time(id) -> double`
4. `Seek(id, time) -> double`
5. `SetLoop(id, loopValue) -> int`

## RGBA 拉帧接口

### 元信息结构

```c
typedef struct RustAVFrameMeta {
    int32_t width;
    int32_t height;
    int32_t format;
    int32_t stride;
    int32_t data_size;
    double  time_sec;
    int64_t frame_index;
} RustAVFrameMeta;
```

字段语义：

1. `width` / `height`：当前导出帧尺寸。
2. `format`：当前固定为 `PIXEL_FORMAT_RGBA32`。
3. `stride`：每行字节数，当前等于 `width * 4`。
4. `data_size`：当前整帧字节数。
5. `time_sec`：帧时间戳，单位秒。
6. `frame_index`：导出帧序号，单调递增。

### 取帧函数

1. `GetFrameMetaRGBA(id, outMeta) -> int`
   - 返回 `1`：已有帧，`outMeta` 已填充。
   - 返回 `0`：播放器有效，但当前还没有首帧。
   - 返回 `-1`：参数无效、`id` 无效，或该播放器不是 RGBA 拉帧模式。
2. `CopyFrameRGBA(id, dst, dstLen) -> int`
   - 返回 `> 0`：实际复制的字节数。
   - 返回 `0`：当前还没有可用帧。
   - 返回 `-1`：参数无效，或目标缓冲区长度不足。

## 推荐调用顺序

### Windows 纹理模式

1. `id = GetPlayer(uri, texturePtr)`
2. `Play(id)`
3. Unity 渲染循环中调用 `UpdatePlayer(id)` 或渲染事件回调
4. 结束时调用 `ReleasePlayer(id)`

### 通用 RGBA 拉帧模式

1. `id = CreatePlayerPullRGBA(uri, width, height)`
2. `Play(id)`
3. 每帧调用：
   - `GetFrameMetaRGBA(id, &meta)`
   - 若返回 `1`，分配或复用 `meta.data_size` 大小的缓冲区
   - `CopyFrameRGBA(id, buffer, bufferLen)`
4. 将数据上传到 Unity `Texture2D`
5. 结束时调用 `ReleasePlayer(id)`

## 线程与内存约束

1. FFI 层建议由 Unity 主线程统一调度，避免跨线程交叉控制同一 `player id`。
2. `CopyFrameRGBA` 会把内部最新帧复制到调用方缓冲区，调用方拿到的是独立副本。
3. `GetFrameMetaRGBA` 只返回最新帧快照，不保证保留历史帧。
4. 若需要固定分辨率贴图，Unity 侧应按创建时的目标宽高分配 `Texture2D`。

## 当前平台约束

1. `GetPlayer` 目前只对 Windows D3D11 有效。
2. `CreatePlayerPullRGBA` 是当前三端统一的最小公共能力。
3. iOS / Android 的实际二进制产物将在后续 GitHub Actions 中生成。
