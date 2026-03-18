using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using DiagnosticsStopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 使用拉帧/拉音频模式的播放器。
    /// 这是 Windows/iOS/Android 的统一主播放路径，优先级高于纹理直连模式。
    /// </summary>
    public class MediaPlayerPull : MonoBehaviour
    {
        private const int DefaultWidth = 1024;
        private const int DefaultHeight = 1024;
        private const int InvalidPlayerId = -1;
        private const int FileAudioStartThresholdMilliseconds = 400;
        private const int RealtimeAudioStartThresholdMilliseconds = 120;
        private const int StreamingAudioClipLengthSeconds = 1800;
        private const int RealtimeAudioStartupGraceMilliseconds = 750;
        private const int RealtimeAudioStartupMinimumThresholdMilliseconds = 40;
        private const int RealtimeStartupAdditionalAudioSinkDelayMilliseconds = 20;
        private const int RealtimeFfmpegAdditionalAudioSinkDelayMilliseconds = 120;
        private const double RealtimeObservedAudioClockClampSeconds = 0.180;
        private const int RealtimeAudioRingCapacityMilliseconds = 750;
        private const int FileAudioBufferedCeilingMilliseconds = 1000;
        private const int RealtimeAudioBufferedCeilingMilliseconds = 60;
        private const int MaxAudioCopyBytes = 64 * 1024;
        private const int MaxAudioCopyIterations = 16;

        private enum RustAVAudioSampleFormat
        {
            Unknown = 0,
            Float32 = 1
        }

        public enum PullVideoRendererKind
        {
            Cpu = 0,
            Wgpu = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVFrameMeta
        {
            public int Width;
            public int Height;
            public int Format;
            public int Stride;
            public int DataSize;
            public double TimeSec;
            public long FrameIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVAudioMeta
        {
            public int SampleRate;
            public int Channels;
            public int BytesPerSample;
            public int SampleFormat;
            public int BufferedBytes;
            public double TimeSec;
            public long FrameIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVStreamInfo
        {
            public uint StructSize;
            public uint StructVersion;
            public int StreamIndex;
            public int CodecType;
            public int Width;
            public int Height;
            public int SampleRate;
            public int Channels;
        }

        public struct PlayerRuntimeHealth
        {
            public int State;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public MediaSourceConnectionState SourceConnectionState;
            public bool IsConnected;
            public bool IsPlaying;
            public bool IsRealtime;
            public bool CanSeek;
            public bool IsLooping;
            public long SourcePacketCount;
            public long SourceTimeoutCount;
            public long SourceReconnectCount;
            public double DurationSec;
            public double SourceLastActivityAgeSec;
            public double CurrentTimeSec;
            public double ExternalTimeSec;
        }

        private enum RustAVPlayerState
        {
            Idle = 0,
            Connecting = 1,
            Ready = 2,
            Playing = 3,
            Paused = 4,
            Shutdown = 5,
            Ended = 6
        }

        private enum RustAVPlayerStopReason
        {
            None = 0,
            UserStop = 1,
            EndOfStream = 2
        }

        /// <summary>
        /// 媒体地址，支持本地文件、RTSP、RTMP。
        /// </summary>
        [Header("Media Properties:")]
        public string Uri;

        /// <summary>
        /// 优先使用的底层后端。
        /// </summary>
        public MediaBackendKind PreferredBackend = MediaBackendKind.Auto;

        /// <summary>
        /// 是否严格要求指定后端；开启后不允许静默回退。
        /// </summary>
        public bool StrictBackend;

        /// <summary>
        /// 是否循环播放。
        /// </summary>
        public bool Loop;

        /// <summary>
        /// 是否在创建后立即播放。
        /// </summary>
        public bool AutoPlay = true;

        /// <summary>
        /// 目标纹理宽度。
        /// </summary>
        [Header("Video Target Properties:")]
        [Range(2, 4096)]
        public int Width = DefaultWidth;

        /// <summary>
        /// 目标纹理高度。
        /// </summary>
        [Range(2, 4096)]
        public int Height = DefaultHeight;

        /// <summary>
        /// 拉帧模式下使用的内部视频渲染器。
        /// Cpu 表示传统 CPU/RGBA 导出路径；
        /// Wgpu 表示使用 Rust 侧 wgpu 渲染后再导出 RGBA。
        /// </summary>
        public PullVideoRendererKind VideoRenderer = PullVideoRendererKind.Cpu;

        /// <summary>
        /// 用于显示视频的材质。
        /// </summary>
        public Material TargetMaterial;

        /// <summary>
        /// 是否启用音频输出。
        /// </summary>
        [Header("Audio Properties:")]
        public bool EnableAudio = true;

        /// <summary>
        /// 是否在缓冲足够后自动启动 Unity 音频播放。
        /// </summary>
        public bool AutoStartAudio = true;

        /// <summary>
        /// 对实时流额外补偿的音频输出延迟，覆盖 Unity 混音线程和设备调度抖动。
        /// </summary>
        [Range(0, 500)]
        public int RealtimeAdditionalAudioSinkDelayMilliseconds = 60;

        private Texture2D _targetTexture;
        private int _id = InvalidPlayerId;
        private long _lastFrameIndex = -1;
        private double _lastPresentedVideoTimeSec = -1.0;
        private byte[] _videoBytes = new byte[0];
        private bool _isRealtimeSource;

        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private byte[] _audioBytes = new byte[0];
        private float[] _audioFloats = new float[0];
        private float[] _audioRing = new float[0];
        private int _audioReadIndex;
        private int _audioWriteIndex;
        private int _audioBufferedSamples;
        private int _audioChannels;
        private int _audioSampleRate;
        private int _audioBytesPerSample;
        private bool _playRequested;
        private bool _resumeAfterPause;
        private MediaBackendKind _actualBackendKind = MediaBackendKind.Auto;
        private PullVideoRendererKind _actualVideoRenderer = PullVideoRendererKind.Cpu;
        private float _playRequestedRealtimeAt = -1f;
        private float _firstVideoFrameRealtimeAt = -1f;
        private float _firstAudioStartRealtimeAt = -1f;
        private float _firstPositivePlaybackTimeRealtimeAt = -1f;
        private double _latestQueuedAudioEndTimeSec = -1.0;
        private double _nextBufferedAudioTimeSec = -1.0;
        private double _audioPlaybackAnchorTimeSec = -1.0;
        private double _audioPlaybackAnchorDspTimeSec = -1.0;
        private int _audioSetPositionCount;
        private int _audioReadCallbackCount;
        private int _audioReadUnderflowCount;
        private int _audioLastReadRequestSamples;
        private int _audioLastReadFilledSamples;
        private int _audioHighWaterSamples;
        private int _nativeBufferedAudioBytes;
        private int _nativeBufferedAudioHighWaterBytes;
        private int _audioLastReportedUnderflowCount;
        private float _nextAudioDiagnosticRealtimeAt;
        private int _videoDiagnosticUpdateCount;
        private int _videoDiagnosticPresentedCount;
        private long _videoDiagnosticPresentedBytes;
        private double _updatePlayerElapsedMsAccum;
        private double _updateVideoFrameElapsedMsAccum;
        private double _updateAudioBufferElapsedMsAccum;
        private float _lastVideoDiagnosticRealtimeAt = -1f;
        private float _nextVideoDiagnosticRealtimeAt;
        private readonly object _audioLock = new object();

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreatePullRGBA")]
        private static extern int CreatePlayerPullRGBA(string uri, int targetWidth, int targetHeight);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreatePullRGBAEx")]
        private static extern int CreatePlayerPullRGBAEx(
            string uri,
            int targetWidth,
            int targetHeight,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateWgpuRGBA")]
        private static extern int CreatePlayerWgpuRGBA(string uri, int targetWidth, int targetHeight);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateWgpuRGBAEx")]
        private static extern int CreatePlayerWgpuRGBAEx(
            string uri,
            int targetWidth,
            int targetHeight,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerRelease")]
        private static extern int ReleasePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerUpdate")]
        private static extern int UpdatePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetDuration")]
        private static extern double Duration(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetTime")]
        private static extern double Time(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerPlay")]
        private static extern int Play(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerStop")]
        private static extern int Stop(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSeek")]
        private static extern int Seek(int id, double time);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSetLoop")]
        private static extern int SetLoop(int id, bool loop);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSetAudioSinkDelaySeconds")]
        private static extern int SetAudioSinkDelaySeconds(int id, double delaySec);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetFrameMetaRGBA")]
        private static extern int GetFrameMetaRGBA(int id, out RustAVFrameMeta outMeta);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCopyFrameRGBA")]
        private static extern int CopyFrameRGBA(int id, byte[] destination, int destinationLength);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetAudioMetaPCM")]
        private static extern int GetAudioMetaPCM(int id, out RustAVAudioMeta outMeta);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCopyAudioPCM")]
        private static extern int CopyAudioPCM(int id, byte[] destination, int destinationLength);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetStreamInfo")]
        private static extern int GetStreamInfo(int id, int streamIndex, ref RustAVStreamInfo outInfo);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetBackendKind")]
        private static extern int GetPlayerBackendKind(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetHealthSnapshotV2")]
        private static extern int GetPlayerHealthSnapshotV2(
            int id,
            ref MediaNativeInteropCommon.RustAVPlayerHealthSnapshotV2 snapshot);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_GetBackendRuntimeDiagnostic")]
        private static extern int GetBackendRuntimeDiagnostic(
            int backendKind,
            string path,
            bool requireAudioExport,
            StringBuilder destination,
            int destinationLength);

        public MediaBackendKind ActualBackendKind
        {
            get { return _actualBackendKind; }
        }

        public PullVideoRendererKind ActualVideoRenderer
        {
            get { return _actualVideoRenderer; }
        }

        public bool HasPresentedVideoFrame
        {
            get { return _lastFrameIndex >= 0; }
        }

        public bool TryGetPresentedVideoTimeSec(out double presentedVideoTimeSec)
        {
            presentedVideoTimeSec = _lastPresentedVideoTimeSec;
            return presentedVideoTimeSec >= 0.0;
        }

        public bool IsAudioOutputActive
        {
            get { return _audioSource != null && _audioSource.isPlaying; }
        }

        public bool HasStartedPlayback
        {
            get
            {
                return HasPresentedVideoFrame
                    || IsAudioOutputActive
                    || _firstPositivePlaybackTimeRealtimeAt >= 0f;
            }
        }

        public bool TryGetEstimatedAudioPresentation(
            out double presentedTimeSec,
            out double pipelineDelaySec)
        {
            presentedTimeSec = -1.0;
            pipelineDelaySec = 0.0;

            if (!EnableAudio || _audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return false;
            }

            var outputDelaySec = ComputeUnityAudioOutputDelaySeconds();
            var pipelineDelayEstimateSec = ComputeUnityAudioPipelineDelaySeconds();
            var latestQueuedAudioEndTimeSec = _latestQueuedAudioEndTimeSec;
            var hasQueuedTailEstimate = latestQueuedAudioEndTimeSec >= 0.0;
            var queuedTailEstimateSec = hasQueuedTailEstimate
                ? Math.Max(0.0, latestQueuedAudioEndTimeSec - pipelineDelayEstimateSec)
                : -1.0;

            double nextBufferedAudioTimeSec;
            lock (_audioLock)
            {
                nextBufferedAudioTimeSec = _nextBufferedAudioTimeSec;
            }

            if (_audioSource != null && _audioSource.isPlaying)
            {
                var dspAnchoredEstimateSec = TryGetDspAnchoredAudioPresentationTimeSec();
                var conservativeEstimateSec = dspAnchoredEstimateSec;
                if (nextBufferedAudioTimeSec >= 0.0)
                {
                    var readHeadEstimateSec = Math.Max(0.0, nextBufferedAudioTimeSec - outputDelaySec);
                    conservativeEstimateSec = conservativeEstimateSec >= 0.0
                        ? Math.Min(conservativeEstimateSec, readHeadEstimateSec)
                        : readHeadEstimateSec;
                }

                if (conservativeEstimateSec >= 0.0)
                {
                    pipelineDelaySec = outputDelaySec;
                    presentedTimeSec = hasQueuedTailEstimate
                        ? Math.Min(conservativeEstimateSec, queuedTailEstimateSec)
                        : conservativeEstimateSec;
                    ClampRealtimeObservedAudioLead(ref presentedTimeSec);
                    return true;
                }
            }

            if (!hasQueuedTailEstimate)
            {
                return false;
            }

            pipelineDelaySec = pipelineDelayEstimateSec;
            presentedTimeSec = queuedTailEstimateSec;
            ClampRealtimeObservedAudioLead(ref presentedTimeSec);
            return true;
        }

        private void ClampRealtimeObservedAudioLead(ref double presentedTimeSec)
        {
            if (!_isRealtimeSource || presentedTimeSec < 0.0 || !ValidatePlayerId(_id))
            {
                return;
            }

            var nativePlaybackTimeSec = Time(_id);
            if (nativePlaybackTimeSec < 0.0)
            {
                return;
            }

            var minAllowedAudioPresentationSec =
                Math.Max(0.0, nativePlaybackTimeSec - RealtimeObservedAudioClockClampSeconds);
            var maxAllowedAudioPresentationSec =
                nativePlaybackTimeSec + RealtimeObservedAudioClockClampSeconds;
            if (presentedTimeSec < minAllowedAudioPresentationSec)
            {
                presentedTimeSec = minAllowedAudioPresentationSec;
            }
            else if (presentedTimeSec > maxAllowedAudioPresentationSec)
            {
                presentedTimeSec = maxAllowedAudioPresentationSec;
            }
        }

        private double TryGetDspAnchoredAudioPresentationTimeSec()
        {
            if (_audioPlaybackAnchorTimeSec < 0.0 || _audioPlaybackAnchorDspTimeSec < 0.0)
            {
                return -1.0;
            }

            var elapsedSec = AudioSettings.dspTime - _audioPlaybackAnchorDspTimeSec;
            if (elapsedSec < -0.050)
            {
                return -1.0;
            }

            return Math.Max(0.0, _audioPlaybackAnchorTimeSec + Math.Max(0.0, elapsedSec));
        }

        private void RefreshAudioPlaybackAnchor()
        {
            if (_audioSource == null || !_audioSource.isPlaying)
            {
                return;
            }

            double nextBufferedAudioTimeSec;
            lock (_audioLock)
            {
                nextBufferedAudioTimeSec = _nextBufferedAudioTimeSec;
            }

            if (nextBufferedAudioTimeSec < 0.0)
            {
                return;
            }

            var outputDelaySec = ComputeUnityAudioOutputDelaySeconds();
            _audioPlaybackAnchorTimeSec = nextBufferedAudioTimeSec;
            _audioPlaybackAnchorDspTimeSec = AudioSettings.dspTime + outputDelaySec;
        }

        private void ResetAudioPlaybackAnchor()
        {
            _audioPlaybackAnchorTimeSec = -1.0;
            _audioPlaybackAnchorDspTimeSec = -1.0;
        }

        public float StartupElapsedSeconds
        {
            get
            {
                if (_playRequestedRealtimeAt < 0f)
                {
                    return 0f;
                }

                return Mathf.Max(0f, UnityEngine.Time.realtimeSinceStartup - _playRequestedRealtimeAt);
            }
        }

        public bool TryGetRuntimeHealth(out PlayerRuntimeHealth health)
        {
            health = default(PlayerRuntimeHealth);
            if (!ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.RuntimeHealthView runtimeHealth;
            if (!MediaNativeInteropCommon.TryReadRuntimeHealth(
                GetPlayerHealthSnapshotV2,
                _id,
                out runtimeHealth))
            {
                return false;
            }

            health = new PlayerRuntimeHealth
            {
                State = runtimeHealth.State,
                RuntimeState = runtimeHealth.RuntimeState,
                PlaybackIntent = runtimeHealth.PlaybackIntent,
                StopReason = runtimeHealth.StopReason,
                SourceConnectionState = runtimeHealth.SourceConnectionState,
                IsConnected = runtimeHealth.IsConnected,
                IsPlaying = runtimeHealth.IsPlaying,
                IsRealtime = runtimeHealth.IsRealtime,
                CanSeek = runtimeHealth.CanSeek,
                IsLooping = runtimeHealth.IsLooping,
                SourcePacketCount = runtimeHealth.SourcePacketCount,
                SourceTimeoutCount = runtimeHealth.SourceTimeoutCount,
                SourceReconnectCount = runtimeHealth.SourceReconnectCount,
                DurationSec = runtimeHealth.DurationSec,
                SourceLastActivityAgeSec = runtimeHealth.SourceLastActivityAgeSec,
                CurrentTimeSec = runtimeHealth.CurrentTimeSec,
                ExternalTimeSec = runtimeHealth.ExternalTimeSec,
            };
            return true;
        }

        /// <summary>
        /// 开始或恢复播放。
        /// </summary>
        public void Play()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Play(_id);
            if (result < 0)
            {
                throw new Exception("Failed to play with error " + result);
            }

            _playRequested = true;
            _playRequestedRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
            TryStartAudioSource();
        }

        /// <summary>
        /// 停止播放。
        /// </summary>
        public void Stop()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Stop(_id);
            if (result < 0)
            {
                throw new Exception("Failed to stop with error " + result);
            }

            _playRequested = false;
            ResetStartupTelemetry();
            if (_audioSource != null)
            {
                _audioSource.Pause();
            }
            UpdateNativeAudioSinkDelay();
        }

        /// <summary>
        /// 获取媒体总时长。
        /// </summary>
        public double Duration()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Duration(_id);
            if (result < 0)
            {
                throw new Exception("Failed to get duration");
            }

            return result;
        }

        /// <summary>
        /// 获取当前播放时间。
        /// </summary>
        public double Time()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Time(_id);
            if (result < 0)
            {
                throw new Exception("Failed to get time");
            }

            return result;
        }

        /// <summary>
        /// 获取主视频流的原始宽高。
        /// </summary>
        public bool TryGetPrimaryVideoSize(out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!ValidatePlayerId(_id))
            {
                return false;
            }

            var info = new RustAVStreamInfo
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVStreamInfo)),
                StructVersion = 1u
            };

            var result = GetStreamInfo(_id, 0, ref info);
            if (result < 0 || info.Width <= 0 || info.Height <= 0)
            {
                return false;
            }

            width = info.Width;
            height = info.Height;
            return true;
        }

        /// <summary>
        /// 执行 seek，并清空 Unity 侧旧音频缓冲。
        /// </summary>
        public void Seek(double time)
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Seek(_id, time);
            if (result < 0)
            {
                throw new Exception("Failed to seek with error " + result);
            }

            ClearAudioBuffer();
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }
            UpdateNativeAudioSinkDelay();
            TryStartAudioSource();
        }

        private IEnumerator Start()
        {
            NativeInitializer.InitializePullOnly(this);

            MediaSourceResolver.PreparedMediaSource preparedSource = null;
            Exception resolveError = null;
            yield return MediaSourceResolver.PreparePlayableSource(
                Uri,
                value => preparedSource = value,
                error => resolveError = error);

            if (resolveError != null)
            {
                throw resolveError;
            }

            var uri = preparedSource.PlaybackUri;
            try
            {
                _isRealtimeSource = preparedSource.IsRealtimeSource;
                EnsureAudioSource();
                var diagnostic = ReadBackendRuntimeDiagnostic(uri);

                _targetTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    name = Uri
                };

                _id = CreateNativePlayer(uri);
                if (!ValidatePlayerId(_id))
                {
                    throw new Exception(
                        "Failed to create pull player with error: " + _id
                        + " requested_backend=" + PreferredBackend
                        + " strict_backend=" + StrictBackend
                        + " diagnostic=" + diagnostic);
                }
                _actualBackendKind = ReadActualBackendKind();
                ResetStartupTelemetry();
                Debug.Log(
                    "[MediaPlayerPull] player_created requested_backend=" + PreferredBackend
                    + " actual_backend=" + _actualBackendKind
                    + " strict_backend=" + StrictBackend
                    + " requested_video_renderer=" + VideoRenderer
                    + " actual_video_renderer=" + _actualVideoRenderer);

                if (TargetMaterial != null)
                {
                    TargetMaterial.mainTexture = _targetTexture;
                }

                SetLoop(_id, Loop);

                if (AutoPlay)
                {
                    Play();
                }
            }
            catch
            {
                ReleaseNativePlayer();
                ReleaseManagedResources();
                throw;
            }
        }

        private void Update()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            _videoDiagnosticUpdateCount += 1;

            var updatePlayerStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdatePlayer(_id);
            _updatePlayerElapsedMsAccum += ElapsedMilliseconds(updatePlayerStartTicks);

            var updateVideoStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdateVideoFrame();
            _updateVideoFrameElapsedMsAccum += ElapsedMilliseconds(updateVideoStartTicks);

            var updateAudioStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdateAudioBuffer();
            _updateAudioBufferElapsedMsAccum += ElapsedMilliseconds(updateAudioStartTicks);
            UpdatePlaybackEndState();
            RecordPositivePlaybackTimeIfNeeded();
            UpdateNativeAudioSinkDelay();
            EmitAudioDiagnostics();
            EmitVideoDiagnostics();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            if (pauseStatus)
            {
                _resumeAfterPause = _playRequested;
                if (_resumeAfterPause)
                {
                    Stop();
                }
            }
            else if (_resumeAfterPause)
            {
                Play();
                _resumeAfterPause = false;
            }
        }

        private void OnDestroy()
        {
            ReleaseNativePlayer();
            ReleaseManagedResources();
        }

        private void OnApplicationQuit()
        {
            ReleaseNativePlayer();
            ReleaseManagedResources();
            NativeInitializer.Teardown();
        }

        private void EnsureAudioSource()
        {
            if (_audioSource != null)
            {
                return;
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
            _audioSource.spatialBlend = 0f;
        }

        private void ResetAudioDiagnostics()
        {
            _audioSetPositionCount = 0;
            _audioReadCallbackCount = 0;
            _audioReadUnderflowCount = 0;
            _audioLastReadRequestSamples = 0;
            _audioLastReadFilledSamples = 0;
            _audioHighWaterSamples = 0;
            _nativeBufferedAudioBytes = 0;
            _nativeBufferedAudioHighWaterBytes = 0;
            _audioLastReportedUnderflowCount = 0;
            _nextAudioDiagnosticRealtimeAt = 0f;
        }

        private void ResetVideoDiagnostics()
        {
            _videoDiagnosticUpdateCount = 0;
            _videoDiagnosticPresentedCount = 0;
            _videoDiagnosticPresentedBytes = 0;
            _updatePlayerElapsedMsAccum = 0.0;
            _updateVideoFrameElapsedMsAccum = 0.0;
            _updateAudioBufferElapsedMsAccum = 0.0;
            _lastVideoDiagnosticRealtimeAt = -1f;
            _nextVideoDiagnosticRealtimeAt = 0f;
        }

        private static double ElapsedMilliseconds(long startTicks)
        {
            return (DiagnosticsStopwatch.GetTimestamp() - startTicks) * 1000.0
                / DiagnosticsStopwatch.Frequency;
        }

        private void ObserveNativeBufferedAudioBytes(int bufferedBytes)
        {
            var normalized = Math.Max(0, bufferedBytes);
            _nativeBufferedAudioBytes = normalized;
            _nativeBufferedAudioHighWaterBytes =
                Math.Max(_nativeBufferedAudioHighWaterBytes, normalized);
        }

        private double BufferedAudioSecondsFromBytes(int bufferedBytes)
        {
            if (bufferedBytes <= 0
                || _audioSampleRate <= 0
                || _audioChannels <= 0
                || _audioBytesPerSample <= 0)
            {
                return 0.0;
            }

            var bytesPerSecond = _audioSampleRate * _audioChannels * _audioBytesPerSample;
            if (bytesPerSecond <= 0)
            {
                return 0.0;
            }

            return (double)bufferedBytes / bytesPerSecond;
        }

        private void EmitAudioDiagnostics()
        {
            if (!EnableAudio || _audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return;
            }

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextAudioDiagnosticRealtimeAt)
            {
                return;
            }

            int callbackCount;
            int underflowCount;
            int requestedSamples;
            int filledSamples;
            int bufferedSamples;
            int highWaterSamples;
            lock (_audioLock)
            {
                callbackCount = _audioReadCallbackCount;
                underflowCount = _audioReadUnderflowCount;
                requestedSamples = _audioLastReadRequestSamples;
                filledSamples = _audioLastReadFilledSamples;
                bufferedSamples = _audioBufferedSamples;
                highWaterSamples = _audioHighWaterSamples;
            }

            var shouldLog = StartupElapsedSeconds <= 20f
                || callbackCount <= 0
                || underflowCount != _audioLastReportedUnderflowCount;
            if (!shouldLog)
            {
                _nextAudioDiagnosticRealtimeAt = now + 1f;
                return;
            }

            var bufferedMilliseconds = 0.0;
            var highWaterMilliseconds = 0.0;
            var nativeBufferedMilliseconds =
                BufferedAudioSecondsFromBytes(_nativeBufferedAudioBytes) * 1000.0;
            var nativeHighWaterMilliseconds =
                BufferedAudioSecondsFromBytes(_nativeBufferedAudioHighWaterBytes) * 1000.0;
            var scale = _audioSampleRate * _audioChannels;
            if (scale > 0)
            {
                bufferedMilliseconds = bufferedSamples * 1000.0 / scale;
                highWaterMilliseconds = highWaterSamples * 1000.0 / scale;
            }

            int dspBufferLength;
            int dspBufferCount;
            AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);

            Debug.Log(
                "[MediaPlayerPull] audio_diag callbacks="
                + callbackCount
                + " underflows=" + underflowCount
                + " last_request=" + requestedSamples
                + " last_filled=" + filledSamples
                + " native_buffered_ms=" + nativeBufferedMilliseconds.ToString("F1")
                + " native_high_water_ms=" + nativeHighWaterMilliseconds.ToString("F1")
                + " buffered_ms=" + bufferedMilliseconds.ToString("F1")
                + " high_water_ms=" + highWaterMilliseconds.ToString("F1")
                + " clip_hz=" + _audioSampleRate
                + " output_hz=" + AudioSettings.outputSampleRate
                + " dsp_buffer=" + dspBufferLength + "x" + dspBufferCount
                + " set_position_count=" + _audioSetPositionCount
                + " is_playing=" + (_audioSource != null && _audioSource.isPlaying));

            _audioLastReportedUnderflowCount = underflowCount;
            _nextAudioDiagnosticRealtimeAt = now + 1f;
        }

        private void EmitVideoDiagnostics()
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (_lastVideoDiagnosticRealtimeAt < 0f)
            {
                _lastVideoDiagnosticRealtimeAt = now;
                _nextVideoDiagnosticRealtimeAt = now + 1f;
                return;
            }

            if (now < _nextVideoDiagnosticRealtimeAt)
            {
                return;
            }

            var elapsedSeconds = Math.Max(0.001f, now - _lastVideoDiagnosticRealtimeAt);
            var updateFps = _videoDiagnosticUpdateCount / elapsedSeconds;
            var presentedFps = _videoDiagnosticPresentedCount / elapsedSeconds;
            var updatePlayerAverageMs = _videoDiagnosticUpdateCount > 0
                ? _updatePlayerElapsedMsAccum / _videoDiagnosticUpdateCount
                : 0.0;
            var updateVideoAverageMs = _videoDiagnosticUpdateCount > 0
                ? _updateVideoFrameElapsedMsAccum / _videoDiagnosticUpdateCount
                : 0.0;
            var updateAudioAverageMs = _videoDiagnosticUpdateCount > 0
                ? _updateAudioBufferElapsedMsAccum / _videoDiagnosticUpdateCount
                : 0.0;
            var uploadMegabytesPerSecond = elapsedSeconds > 0f
                ? (_videoDiagnosticPresentedBytes / 1024.0 / 1024.0) / elapsedSeconds
                : 0.0;

            Debug.Log(
                "[MediaPlayerPull] video_diag update_fps="
                + updateFps.ToString("F1")
                + " presented_fps=" + presentedFps.ToString("F1")
                + " update_player_ms_avg=" + updatePlayerAverageMs.ToString("F2")
                + " update_video_ms_avg=" + updateVideoAverageMs.ToString("F2")
                + " update_audio_ms_avg=" + updateAudioAverageMs.ToString("F2")
                + " upload_mib_per_sec=" + uploadMegabytesPerSecond.ToString("F1")
                + " frame_index=" + _lastFrameIndex
                + " frame_time=" + _lastPresentedVideoTimeSec.ToString("F3")
                + " texture=" + HasPresentedVideoFrame);

            _videoDiagnosticUpdateCount = 0;
            _videoDiagnosticPresentedCount = 0;
            _videoDiagnosticPresentedBytes = 0;
            _updatePlayerElapsedMsAccum = 0.0;
            _updateVideoFrameElapsedMsAccum = 0.0;
            _updateAudioBufferElapsedMsAccum = 0.0;
            _lastVideoDiagnosticRealtimeAt = now;
            _nextVideoDiagnosticRealtimeAt = now + 1f;
        }

        private static bool ValidatePlayerId(int id)
        {
            return id >= 0;
        }

        private void UpdateVideoFrame()
        {
            RustAVFrameMeta meta;
            var status = GetFrameMetaRGBA(_id, out meta);
            if (status <= 0 || meta.FrameIndex == _lastFrameIndex || meta.DataSize <= 0)
            {
                return;
            }

            if (_videoBytes.Length != meta.DataSize)
            {
                _videoBytes = new byte[meta.DataSize];
            }

            var copied = CopyFrameRGBA(_id, _videoBytes, _videoBytes.Length);
            if (copied != meta.DataSize)
            {
                return;
            }

            _targetTexture.LoadRawTextureData(_videoBytes);
            _targetTexture.Apply(false, false);
            _lastFrameIndex = meta.FrameIndex;
            _lastPresentedVideoTimeSec = meta.TimeSec;
            _videoDiagnosticPresentedCount += 1;
            _videoDiagnosticPresentedBytes += meta.DataSize;
            if (_firstVideoFrameRealtimeAt < 0f)
            {
                _firstVideoFrameRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
                Debug.Log(
                    "[MediaPlayerPull] first_video_frame startup_seconds="
                    + StartupElapsedSeconds.ToString("F3")
                    + " frame_time="
                    + meta.TimeSec.ToString("F3"));
            }
        }

        private void UpdateAudioBuffer()
        {
            if (!EnableAudio || !ValidatePlayerId(_id))
            {
                return;
            }

            var latestNativeBufferedBytes = 0;
            for (var iteration = 0; iteration < MaxAudioCopyIterations; iteration++)
            {
                RustAVAudioMeta meta;
                var status = GetAudioMetaPCM(_id, out meta);
                if (status <= 0 || meta.BufferedBytes <= 0)
                {
                    latestNativeBufferedBytes = 0;
                    break;
                }

                if (!EnsureAudioFormat(meta))
                {
                    break;
                }

                latestNativeBufferedBytes = meta.BufferedBytes;

                var maxBufferedSamples = CalculateBufferedAudioCeilingSamples();
                var bufferedSamples = 0;
                lock (_audioLock)
                {
                    bufferedSamples = _audioBufferedSamples;
                }
                if (maxBufferedSamples > 0 && bufferedSamples >= maxBufferedSamples)
                {
                    break;
                }

                var bytesPerInterleavedSample = meta.BytesPerSample * meta.Channels;
                if (bytesPerInterleavedSample <= 0)
                {
                    break;
                }

                var bytesToCopy = Math.Min(meta.BufferedBytes, MaxAudioCopyBytes);
                if (maxBufferedSamples > 0)
                {
                    var remainingSamples = Math.Max(0, maxBufferedSamples - bufferedSamples);
                    var remainingBytes = remainingSamples * meta.BytesPerSample;
                    if (remainingBytes <= 0)
                    {
                        break;
                    }

                    bytesToCopy = Math.Min(bytesToCopy, remainingBytes);
                }
                bytesToCopy -= bytesToCopy % bytesPerInterleavedSample;
                if (bytesToCopy <= 0)
                {
                    break;
                }

                if (_audioBytes.Length != bytesToCopy)
                {
                    _audioBytes = new byte[bytesToCopy];
                }

                var copied = CopyAudioPCM(_id, _audioBytes, _audioBytes.Length);
                if (copied <= 0)
                {
                    break;
                }

                latestNativeBufferedBytes = Math.Max(0, meta.BufferedBytes - copied);

                var sampleCount = copied / meta.BytesPerSample;
                if (_audioFloats.Length != sampleCount)
                {
                    _audioFloats = new float[sampleCount];
                }

                Buffer.BlockCopy(_audioBytes, 0, _audioFloats, 0, copied);
                WriteAudioSamples(_audioFloats, sampleCount, meta.TimeSec);

                if (copied < bytesToCopy)
                {
                    break;
                }
            }

            ObserveNativeBufferedAudioBytes(latestNativeBufferedBytes);
            TryStartAudioSource();
        }

        private void UpdatePlaybackEndState()
        {
            if (_isRealtimeSource || !ValidatePlayerId(_id) || _audioSource == null || !_audioSource.isPlaying)
            {
                return;
            }

            PlayerRuntimeHealth health;
            if (!TryGetRuntimeHealth(out health) || health.IsLooping)
            {
                return;
            }

            var reachedEndOfStream = health.StopReason == (int)RustAVPlayerStopReason.EndOfStream
                || health.State == (int)RustAVPlayerState.Ended
                || health.RuntimeState == (int)RustAVPlayerState.Ended
                || (!health.IsPlaying
                    && health.DurationSec > 0.0
                    && health.CurrentTimeSec >= health.DurationSec - 0.050);
            if (!reachedEndOfStream)
            {
                return;
            }

            var bufferedSamples = 0;
            lock (_audioLock)
            {
                bufferedSamples = _audioBufferedSamples;
            }

            if (_nativeBufferedAudioBytes > 0 || bufferedSamples > 0)
            {
                return;
            }

            _audioSource.Stop();
            ClearAudioBuffer();
            _playRequested = false;
            Debug.Log(
                "[MediaPlayerPull] playback_ended unity_audio_stopped current_time="
                + health.CurrentTimeSec.ToString("F3")
                + " duration="
                + health.DurationSec.ToString("F3")
                + " stop_reason="
                + health.StopReason);
        }

        private bool EnsureAudioFormat(RustAVAudioMeta meta)
        {
            if (meta.SampleRate <= 0
                || meta.Channels <= 0
                || meta.BytesPerSample != 4
                || meta.SampleFormat != (int)RustAVAudioSampleFormat.Float32)
            {
                return false;
            }

            if (_audioClip != null
                && _audioSampleRate == meta.SampleRate
                && _audioChannels == meta.Channels
                && _audioBytesPerSample == meta.BytesPerSample)
            {
                return true;
            }

            _audioSampleRate = meta.SampleRate;
            _audioChannels = meta.Channels;
            _audioBytesPerSample = meta.BytesPerSample;

            var ringCapacity = Math.Max(_audioSampleRate * _audioChannels * 4, 4096);
            if (_isRealtimeSource)
            {
                ringCapacity = Math.Max(
                    (_audioSampleRate * _audioChannels
                        * RealtimeAudioRingCapacityMilliseconds) / 1000,
                    4096);
            }
            lock (_audioLock)
            {
                _audioRing = new float[ringCapacity];
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                _audioHighWaterSamples = 0;
                _latestQueuedAudioEndTimeSec = -1.0;
                _nextBufferedAudioTimeSec = -1.0;
            }
            ResetAudioPlaybackAnchor();
            ResetAudioDiagnostics();

            if (_audioClip != null)
            {
                Destroy(_audioClip);
            }

            _audioClip = AudioClip.Create(
                Uri + "_PullAudio",
                _audioSampleRate * StreamingAudioClipLengthSeconds,
                _audioChannels,
                _audioSampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);

            _audioSource.clip = _audioClip;
            _audioSource.loop = true;
            return true;
        }

        private void WriteAudioSamples(float[] samples, int sampleCount, double chunkStartTimeSec)
        {
            if (samples == null || sampleCount <= 0)
            {
                return;
            }

            lock (_audioLock)
            {
                if (_audioRing == null || _audioRing.Length == 0)
                {
                    return;
                }

                var secondsPerSample = SecondsPerInterleavedSample();
                var canTrackAudioTime = chunkStartTimeSec >= 0.0 && secondsPerSample > 0.0;
                if (canTrackAudioTime && (_nextBufferedAudioTimeSec < 0.0 || _audioBufferedSamples <= 0))
                {
                    _nextBufferedAudioTimeSec = chunkStartTimeSec;
                }

                if (sampleCount >= _audioRing.Length)
                {
                    Array.Copy(
                        samples,
                        sampleCount - _audioRing.Length,
                        _audioRing,
                        0,
                        _audioRing.Length);
                    _audioReadIndex = 0;
                    _audioWriteIndex = 0;
                    _audioBufferedSamples = _audioRing.Length;
                    if (canTrackAudioTime)
                    {
                        _nextBufferedAudioTimeSec = chunkStartTimeSec
                            + (sampleCount - _audioRing.Length) * secondsPerSample;
                    }
                    TrimBufferedAudioSamplesIfNeeded();
                    RefreshBufferedAudioTailLocked();
                    return;
                }

                var freeSamples = _audioRing.Length - _audioBufferedSamples;
                if (sampleCount > freeSamples)
                {
                    var dropSamples = sampleCount - freeSamples;
                    _audioReadIndex = (_audioReadIndex + dropSamples) % _audioRing.Length;
                    _audioBufferedSamples -= dropSamples;
                    AdvanceBufferedAudioHeadLocked(dropSamples);
                }

                var firstCopy = Math.Min(sampleCount, _audioRing.Length - _audioWriteIndex);
                Array.Copy(samples, 0, _audioRing, _audioWriteIndex, firstCopy);

                var secondCopy = sampleCount - firstCopy;
                if (secondCopy > 0)
                {
                    Array.Copy(samples, firstCopy, _audioRing, 0, secondCopy);
                }

                _audioWriteIndex = (_audioWriteIndex + sampleCount) % _audioRing.Length;
                _audioBufferedSamples += sampleCount;
                TrimBufferedAudioSamplesIfNeeded();
                _audioHighWaterSamples = Math.Max(_audioHighWaterSamples, _audioBufferedSamples);
                RefreshBufferedAudioTailLocked();
            }
        }

        private void TrimBufferedAudioSamplesIfNeeded()
        {
            if (_audioRing == null || _audioRing.Length == 0)
            {
                return;
            }

            var maxBufferedSamples = CalculateBufferedAudioCeilingSamples();
            if (maxBufferedSamples <= 0 || _audioBufferedSamples <= maxBufferedSamples)
            {
                return;
            }

            var dropSamples = _audioBufferedSamples - maxBufferedSamples;
            _audioReadIndex = (_audioReadIndex + dropSamples) % _audioRing.Length;
            _audioBufferedSamples = maxBufferedSamples;
            AdvanceBufferedAudioHeadLocked(dropSamples);
            RefreshBufferedAudioTailLocked();
        }

        private int CalculateBufferedAudioCeilingSamples()
        {
            if (_audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return 0;
            }

            var steadyStateMilliseconds = _isRealtimeSource
                ? RealtimeAudioBufferedCeilingMilliseconds
                : FileAudioBufferedCeilingMilliseconds;
            var steadyStateSamples = (_audioSampleRate * _audioChannels * steadyStateMilliseconds) / 1000;
            steadyStateSamples = Math.Max(steadyStateSamples, _audioChannels);

            if (_audioSource != null && _audioSource.isPlaying)
            {
                return steadyStateSamples;
            }

            return CalculateAudioStartThresholdSamples();
        }

        private void OnAudioRead(float[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            Array.Clear(data, 0, data.Length);

            lock (_audioLock)
            {
                _audioReadCallbackCount += 1;
                _audioLastReadRequestSamples = data.Length;
                if (_audioBufferedSamples <= 0 || _audioRing == null || _audioRing.Length == 0)
                {
                    _audioLastReadFilledSamples = 0;
                    _audioReadUnderflowCount += 1;
                    return;
                }

                var samplesToRead = Math.Min(data.Length, _audioBufferedSamples);
                var firstCopy = Math.Min(samplesToRead, _audioRing.Length - _audioReadIndex);
                Array.Copy(_audioRing, _audioReadIndex, data, 0, firstCopy);

                var secondCopy = samplesToRead - firstCopy;
                if (secondCopy > 0)
                {
                    Array.Copy(_audioRing, 0, data, firstCopy, secondCopy);
                }

                _audioReadIndex = (_audioReadIndex + samplesToRead) % _audioRing.Length;
                _audioBufferedSamples -= samplesToRead;
                _audioLastReadFilledSamples = samplesToRead;
                if (samplesToRead < data.Length)
                {
                    _audioReadUnderflowCount += 1;
                }
                AdvanceBufferedAudioHeadLocked(samplesToRead);
                RefreshBufferedAudioTailLocked();
            }
        }

        private void OnAudioSetPosition(int position)
        {
            _audioSetPositionCount += 1;
            if (_audioSetPositionCount <= 3)
            {
                Debug.Log(
                    "[MediaPlayerPull] audio_set_position position="
                    + position
                    + " count=" + _audioSetPositionCount);
            }
        }

        private void ClearAudioBuffer()
        {
            lock (_audioLock)
            {
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                _audioHighWaterSamples = 0;
                _latestQueuedAudioEndTimeSec = -1.0;
                _nextBufferedAudioTimeSec = -1.0;
                if (_audioRing != null && _audioRing.Length > 0)
                {
                    Array.Clear(_audioRing, 0, _audioRing.Length);
                }
            }

            ResetAudioPlaybackAnchor();
            ResetAudioDiagnostics();
            UpdateNativeAudioSinkDelay();
        }

        private void TryStartAudioSource()
        {
            if (!EnableAudio || !AutoStartAudio || !_playRequested || _audioSource == null || _audioClip == null)
            {
                return;
            }

            if (_audioSource.isPlaying)
            {
                return;
            }

            lock (_audioLock)
            {
                if (_audioSampleRate <= 0 || _audioChannels <= 0)
                {
                    return;
                }

                if (_isRealtimeSource && !HasPresentedVideoFrame)
                {
                    return;
                }

                var thresholdSamples = CalculateAudioStartThresholdSamples();
                if (_audioBufferedSamples < thresholdSamples)
                {
                    return;
                }
            }

            _audioSource.Play();
            RefreshAudioPlaybackAnchor();
            if (_firstAudioStartRealtimeAt < 0f)
            {
                _firstAudioStartRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
                Debug.Log(
                    "[MediaPlayerPull] first_audio_start startup_seconds="
                    + StartupElapsedSeconds.ToString("F3"));
            }
            UpdateNativeAudioSinkDelay();
        }

        private int CalculateAudioStartThresholdSamples()
        {
            var thresholdMilliseconds = _isRealtimeSource
                ? RealtimeAudioStartThresholdMilliseconds
                : FileAudioStartThresholdMilliseconds;
            var thresholdSamples = (_audioSampleRate * _audioChannels
                * thresholdMilliseconds) / 1000;
            if (!_isRealtimeSource)
            {
                return thresholdSamples;
            }

            var startupElapsedMilliseconds = StartupElapsedSeconds * 1000f;
            if (!HasPresentedVideoFrame
                || startupElapsedMilliseconds < RealtimeAudioStartupGraceMilliseconds)
            {
                return thresholdSamples;
            }

            var relaxedThresholdSamples = (_audioSampleRate * _audioChannels
                * RealtimeAudioStartupMinimumThresholdMilliseconds) / 1000;
            return Math.Max(Math.Min(thresholdSamples, relaxedThresholdSamples), _audioChannels);
        }

        private void RecordPositivePlaybackTimeIfNeeded()
        {
            if (_firstPositivePlaybackTimeRealtimeAt >= 0f || !ValidatePlayerId(_id))
            {
                return;
            }

            double playbackTime;
            try
            {
                playbackTime = Time(_id);
            }
            catch (Exception)
            {
                return;
            }

            if (playbackTime <= 0.0)
            {
                return;
            }

            _firstPositivePlaybackTimeRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
            Debug.Log(
                "[MediaPlayerPull] first_positive_playback_time startup_seconds="
                + StartupElapsedSeconds.ToString("F3")
                + " playback_time="
                + playbackTime.ToString("F3"));
        }

        private void UpdateNativeAudioSinkDelay()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            SetAudioSinkDelaySeconds(_id, ComputeUnityAudioPipelineDelaySeconds());
        }

        private double ComputeUnityAudioPipelineDelaySeconds()
        {
            var delaySec = 0.0;
            var audioStarted = _audioSource != null && _audioSource.isPlaying;
            if (EnableAudio && _audioSampleRate > 0 && _audioChannels > 0)
            {
                delaySec += BufferedAudioSecondsFromBytes(_nativeBufferedAudioBytes);
                if (!_isRealtimeSource || audioStarted)
                {
                    lock (_audioLock)
                    {
                        delaySec += (double)_audioBufferedSamples / (_audioSampleRate * _audioChannels);
                    }
                }

                int dspBufferLength;
                int dspBufferCount;
                AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);
                if (dspBufferLength > 0 && dspBufferCount > 0)
                {
                    delaySec += (double)(dspBufferLength * dspBufferCount) / _audioSampleRate;
                }
            }

            var realtimeAdditionalDelayMilliseconds =
                GetRealtimeAdditionalAudioSinkDelayMilliseconds(audioStarted);
            if (realtimeAdditionalDelayMilliseconds > 0)
            {
                delaySec += (double)realtimeAdditionalDelayMilliseconds / 1000.0;
            }

            return delaySec;
        }

        private double ComputeUnityAudioOutputDelaySeconds()
        {
            var delaySec = 0.0;
            if (EnableAudio && _audioSampleRate > 0 && _audioChannels > 0)
            {
                int dspBufferLength;
                int dspBufferCount;
                AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);
                if (dspBufferLength > 0 && dspBufferCount > 0)
                {
                    delaySec += (double)(dspBufferLength * dspBufferCount) / _audioSampleRate;
                }
            }

            var realtimeAdditionalDelayMilliseconds =
                GetRealtimeAdditionalAudioSinkDelayMilliseconds(true);
            if (realtimeAdditionalDelayMilliseconds > 0)
            {
                delaySec += (double)realtimeAdditionalDelayMilliseconds / 1000.0;
            }

            return delaySec;
        }

        private int GetRealtimeAdditionalAudioSinkDelayMilliseconds(bool audioStarted)
        {
            if (!_isRealtimeSource)
            {
                return 0;
            }

            var delayMilliseconds = audioStarted
                ? RealtimeAdditionalAudioSinkDelayMilliseconds
                : RealtimeStartupAdditionalAudioSinkDelayMilliseconds;
            if (_actualBackendKind == MediaBackendKind.Ffmpeg)
            {
                delayMilliseconds += RealtimeFfmpegAdditionalAudioSinkDelayMilliseconds;
            }

            return delayMilliseconds;
        }

        private double SecondsPerInterleavedSample()
        {
            if (_audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return 0.0;
            }

            return 1.0 / (_audioSampleRate * _audioChannels);
        }

        private void RefreshBufferedAudioTailLocked()
        {
            if (_audioBufferedSamples <= 0 || _nextBufferedAudioTimeSec < 0.0)
            {
                _latestQueuedAudioEndTimeSec = -1.0;
                return;
            }

            var secondsPerSample = SecondsPerInterleavedSample();
            if (secondsPerSample <= 0.0)
            {
                _latestQueuedAudioEndTimeSec = -1.0;
                return;
            }

            _latestQueuedAudioEndTimeSec =
                _nextBufferedAudioTimeSec + _audioBufferedSamples * secondsPerSample;
        }

        private void AdvanceBufferedAudioHeadLocked(int consumedSamples)
        {
            if (consumedSamples <= 0 || _nextBufferedAudioTimeSec < 0.0)
            {
                return;
            }

            var secondsPerSample = SecondsPerInterleavedSample();
            if (secondsPerSample <= 0.0)
            {
                return;
            }

            _nextBufferedAudioTimeSec += consumedSamples * secondsPerSample;
        }

        private void ResetStartupTelemetry()
        {
            _playRequestedRealtimeAt = -1f;
            _firstVideoFrameRealtimeAt = -1f;
            _firstAudioStartRealtimeAt = -1f;
            _firstPositivePlaybackTimeRealtimeAt = -1f;
            ResetAudioPlaybackAnchor();
            ResetVideoDiagnostics();
        }

        private void ReleaseNativePlayer()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            ResetAudioPlaybackAnchor();
            SetAudioSinkDelaySeconds(_id, 0.0);
            ReleasePlayer(_id);
            _id = InvalidPlayerId;
            _actualBackendKind = MediaBackendKind.Auto;
            _actualVideoRenderer = PullVideoRendererKind.Cpu;
            _playRequested = false;
            _resumeAfterPause = false;
            ResetStartupTelemetry();
        }

        private void ReleaseManagedResources()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            ResetAudioPlaybackAnchor();

            if (TargetMaterial != null && ReferenceEquals(TargetMaterial.mainTexture, _targetTexture))
            {
                TargetMaterial.mainTexture = null;
            }

            if (_audioClip != null)
            {
                Destroy(_audioClip);
                _audioClip = null;
            }

            if (_targetTexture != null)
            {
                Destroy(_targetTexture);
                _targetTexture = null;
            }

            _videoBytes = new byte[0];
            _lastFrameIndex = -1;
            _lastPresentedVideoTimeSec = -1.0;
            _audioBytes = new byte[0];
            _audioFloats = new float[0];
            _latestQueuedAudioEndTimeSec = -1.0;
            ResetAudioPlaybackAnchor();
            lock (_audioLock)
            {
                _audioRing = new float[0];
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                _audioHighWaterSamples = 0;
                _nextBufferedAudioTimeSec = -1.0;
            }
            ResetAudioDiagnostics();
            ResetVideoDiagnostics();
        }

        private int CreateNativePlayer(string uri)
        {
            try
            {
                var options = MediaNativeInteropCommon.CreateOpenOptions(
                    PreferredBackend,
                    StrictBackend);
                if (VideoRenderer == PullVideoRendererKind.Wgpu)
                {
                    _actualVideoRenderer = PullVideoRendererKind.Wgpu;
                    return CreatePlayerWgpuRGBAEx(uri, Width, Height, ref options);
                }

                _actualVideoRenderer = PullVideoRendererKind.Cpu;
                return CreatePlayerPullRGBAEx(uri, Width, Height, ref options);
            }
            catch (EntryPointNotFoundException)
            {
                if (VideoRenderer == PullVideoRendererKind.Wgpu)
                {
                    try
                    {
                        _actualVideoRenderer = PullVideoRendererKind.Wgpu;
                        return CreatePlayerWgpuRGBA(uri, Width, Height);
                    }
                    catch (EntryPointNotFoundException)
                    {
                        Debug.LogWarning(
                            "[MediaPlayerPull] wgpu entrypoint missing, fallback to cpu renderer");
                    }
                }

                _actualVideoRenderer = PullVideoRendererKind.Cpu;
                return CreatePlayerPullRGBA(uri, Width, Height);
            }
        }

        private string ReadBackendRuntimeDiagnostic(string uri)
        {
            return MediaNativeInteropCommon.ReadBackendRuntimeDiagnostic(
                GetBackendRuntimeDiagnostic,
                PreferredBackend,
                uri,
                EnableAudio);
        }

        private MediaBackendKind ReadActualBackendKind()
        {
            try
            {
                return MediaNativeInteropCommon.NormalizeBackendKind(
                    GetPlayerBackendKind(_id),
                    PreferredBackend);
            }
            catch (EntryPointNotFoundException)
            {
                return PreferredBackend;
            }
        }
    }
}
