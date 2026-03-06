# 低延迟推流基线

## 当前环境

1. MediaMTX：`C:\Users\HP\Downloads\mediamtx_v1.16.3_windows_amd64\mediamtx.exe`
2. 本机测试流：
   - RTSP 读地址：`rtsp://localhost:8554/mystream`
   - RTMP 读地址：`rtmp://localhost:1935/mystream`
3. 当前已验证：同一路 `mystream` 由 MediaMTX 跨协议分发，RustAV 可同时作为 RTSP/RTMP reader 读取。

## 结论

之前的主要瓶颈不是 RustAV 单侧，而是推流端把源文件里的编码结构原样带进来了。  
源文件 `SampleVideo.mp4` 当前特征：

1. `24fps`
2. `has_b_frames=1`

这会天然拉高首帧时间和端到端延迟。

## 已验证的低延迟推流命令

### RTSP 发布到 MediaMTX

```bat
ffmpeg.exe -re -stream_loop -1 -i SampleVideo.mp4 ^
  -map 0:v:0 -an ^
  -c:v libx264 -preset ultrafast -tune zerolatency ^
  -profile:v baseline -pix_fmt yuv420p ^
  -g 3 -keyint_min 3 -sc_threshold 0 -bf 0 -refs 1 ^
  -b:v 2500k -maxrate 2500k -bufsize 100k ^
  -x264-params "rc-lookahead=0:sync-lookahead=0:repeat-headers=1:force-cfr=1" ^
  -f rtsp -rtsp_transport udp -muxdelay 0 -muxpreload 0 ^
  rtsp://127.0.0.1:8554/mystream
```

### RTMP 发布到 MediaMTX

```bat
ffmpeg.exe -re -stream_loop -1 -i SampleVideo.mp4 ^
  -map 0:v:0 -an ^
  -c:v libx264 -preset ultrafast -tune zerolatency ^
  -profile:v baseline -pix_fmt yuv420p ^
  -g 3 -keyint_min 3 -sc_threshold 0 -bf 0 -refs 1 ^
  -b:v 2500k -maxrate 2500k -bufsize 100k ^
  -x264-params "rc-lookahead=0:sync-lookahead=0:repeat-headers=1:force-cfr=1" ^
  -f flv rtmp://127.0.0.1:1935/mystream
```

## 参数解释

1. `-preset ultrafast`：尽量减少编码器内部缓存。
2. `-tune zerolatency`：关闭一批会增加等待的编码器行为。
3. `-bf 0`：禁用 B 帧。
4. `-g 3 -keyint_min 3`：24fps 下约每 `125ms` 一个 IDR。
5. `-sc_threshold 0`：避免场景切换插入不稳定 GOP。
6. `-refs 1`：减少解码端参考帧依赖。
7. `-bufsize 100k`：进一步缩小码率控制缓冲。
8. `-an`：当前 RustAV 只测视频，去掉音频有利于降低 mux 侧延迟。
9. `-rtsp_transport udp`：RTSP 推流走 UDP，减少 TCP 传输抖动带来的积压。
10. `-muxdelay 0 -muxpreload 0`：尽量避免输出 mux 侧等待。

## 当前观测

在以上推流参数配合下，RustAV 当前 10 秒探测结果：

1. RTSP：`234` 帧
2. RTMP：`236` 帧

这说明吞吐已经接近实时 24fps。

## 关于 200ms 目标

当前推流端已经把最关键的编码侧延迟项压掉了，但是否稳定进入 `<200ms` 还要看：

1. 首帧统计值
2. 读端收到的实际 PTS 与本地时钟差
3. MediaMTX 内部转发开销

因此，后续判断是否真正达标，必须看 `rtsp_probe` / `rtmp_probe` 里的 `first_frame` 指标，而不是只看总帧数。
