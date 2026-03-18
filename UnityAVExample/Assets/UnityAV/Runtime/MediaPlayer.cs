using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using DiagnosticsStopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UnityAV
{
    /// <summary>
    /// 使用原生纹理直连模式的播放器。
    /// 当前主要服务 Windows 纹理互操作增强场景。
    /// iOS/Android 侧当前只收口 NativeVideo 契约与激活决策，不作为 Android/iOS 主播放入口。
    /// </summary>
    public class MediaPlayer : MonoBehaviour
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
        private const int RealtimeAudioRingCapacityMilliseconds = 750;
        private const int FileAudioBufferedCeilingMilliseconds = 1000;
        private const int RealtimeAudioBufferedCeilingMilliseconds = 60;
        private const int FileNativeVideoStartupWarmupStableFrames = 4;
        private const int MaxAudioCopyBytes = 64 * 1024;
        private const int MaxAudioCopyIterations = 16;

        public enum NativeVideoPresentationPathKind
        {
            None = 0,
            DirectBind = 1,
            DirectShader = 2,
            Compute = 3,
            RenderEventPass = 4,
        }

        public enum NativeVideoActivationDecisionKind
        {
            NotRequested = 0,
            InvalidTarget = 1,
            CapsUnavailable = 2,
            UnsupportedTarget = 3,
            HardwareDecodeUnavailable = 4,
            StrictZeroCopyUnavailable = 5,
            AcquireReleaseUnavailable = 6,
            CreateFailed = 7,
            Active = 8,
            PlatformRuntimeUnavailable = 9,
        }

        public struct PlayerRuntimeHealth
        {
            public MediaSourceConnectionState SourceConnectionState;
            public bool IsConnected;
            public bool IsPlaying;
            public bool IsRealtime;
            public long SourcePacketCount;
            public long SourceTimeoutCount;
            public long SourceReconnectCount;
            public double SourceLastActivityAgeSec;
            public double CurrentTimeSec;
        }

        public struct NativeVideoInteropInfo
        {
            public MediaBackendKind BackendKind;
            public NativeVideoPlatformKind PlatformKind;
            public NativeVideoSurfaceKind SurfaceKind;
            public bool Supported;
            public bool ContractTargetSupported;
            public bool HardwareDecodeSupported;
            public bool ZeroCopySupported;
            public bool SourceSurfaceZeroCopySupported;
            public bool ExternalTextureTarget;
            public bool FallbackCopyPathSupported;
            public bool PresentedFrameDirectBindable;
            public bool PresentedFrameStrictZeroCopySupported;
            public bool SourcePlaneTexturesSupported;
            public bool SourcePlaneViewsSupported;
            public bool AcquireReleaseSupported;
            public bool RuntimeBridgePending;
            public uint Flags;
        }

        public struct NativeVideoFrameInfo
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormatKind PixelFormat;
            public IntPtr NativeHandle;
            public IntPtr AuxiliaryHandle;
            public int Width;
            public int Height;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        public struct NativeVideoFrameCadenceSnapshot
        {
            public long AcquireAttemptCount;
            public long AcquireMissCount;
            public long PresentCount;
            public long SampleCount;
            public long DuplicateCount;
            public long MaxDuplicateStreak;
            public long MaxAcquireMissStreak;
            public long SkippedFrameCount;
            public long NonMonotonicCount;
            public long LastFrameIndexDelta;
            public double LastFrameTimeDeltaSec;
            public double LastRealtimeDeltaSec;
            public double MinFrameTimeDeltaSec;
            public double MaxFrameTimeDeltaSec;
            public double AvgFrameTimeDeltaSec;
            public double MinRealtimeDeltaSec;
            public double MaxRealtimeDeltaSec;
            public double AvgRealtimeDeltaSec;
            public long PresentationFailureCount;
            public long RenderEventPassCount;
            public long DirectBindCount;
            public long DirectShaderCount;
            public long ComputeCount;
        }

        public struct NativeVideoUpdateTimingSnapshot
        {
            public long UpdateCount;
            public double UpdatePlayerElapsedMsAvg;
            public double UpdatePlayerElapsedMsMax;
            public double UpdateNativeVideoFrameElapsedMsAvg;
            public double UpdateNativeVideoFrameElapsedMsMax;
            public double UpdateAudioBufferElapsedMsAvg;
            public double UpdateAudioBufferElapsedMsMax;
        }

        public struct NativeVideoPresentationTelemetrySnapshot
        {
            public long RenderEventPassAttemptCount;
            public long RenderEventPassSuccessCount;
            public long DirectBindAttemptCount;
            public long DirectBindSuccessCount;
            public long DirectShaderAttemptCount;
            public long DirectShaderSuccessCount;
            public long DirectShaderSourcePlaneTexturesUnsupportedCount;
            public long DirectShaderShaderUnavailableCount;
            public long DirectShaderAcquireSourcePlaneTexturesFailureCount;
            public long DirectShaderPlaneTexturesUsabilityFailureCount;
            public long DirectShaderMaterialFailureCount;
            public long DirectShaderExceptionCount;
            public long ComputeAttemptCount;
            public long ComputeSuccessCount;
            public long ComputeSourcePlaneTexturesUnsupportedCount;
            public long ComputeShaderUnavailableCount;
            public long ComputeAcquireSourcePlaneTexturesFailureCount;
            public long ComputePlaneTexturesUsabilityFailureCount;
            public long ComputeExceptionCount;
        }

        public struct NativeVideoColorInfo
        {
            public NativeVideoColorRangeKind Range;
            public NativeVideoColorMatrixKind Matrix;
            public NativeVideoColorPrimariesKind Primaries;
            public NativeVideoTransferCharacteristicKind Transfer;
            public int BitDepth;
            public NativeVideoDynamicRangeKind DynamicRange;
        }

        public struct NativeVideoPlaneTexturesInfo
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormatKind SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormatKind YTextureFormat;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormatKind UVTextureFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        public struct NativeVideoPlaneViewsInfo
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormatKind SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormatKind YTextureFormat;
            public NativeVideoPlaneResourceKindKind YResourceKind;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormatKind UVTextureFormat;
            public NativeVideoPlaneResourceKindKind UVResourceKind;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        private enum RustAVAudioSampleFormat
        {
            Unknown = 0,
            Float32 = 1
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

        /// <summary>
        /// The uri of the media to stream
        /// </summary>
        [Header("Media Properties:")]
        public string Uri;

        /// <summary>
        /// Preferred native backend.
        /// </summary>
        public MediaBackendKind PreferredBackend = MediaBackendKind.Auto;

        /// <summary>
        /// Whether fallback is forbidden when PreferredBackend is specified.
        /// </summary>
        public bool StrictBackend;

        /// <summary>
        /// Should the media be looped?
        /// </summary>
        public bool Loop;

        /// <summary>
        /// 是否输出逐帧的 native video 取帧节奏日志。
        /// </summary>
        public bool TraceNativeVideoCadence;

        /// <summary>
        /// Should the media play as soon as it's loaded?
        /// </summary>
        public bool AutoPlay;

        /// <summary>
        /// The width of the texture in pixels
        /// </summary>
        [Header("Video Target Properties:")]
        [Range(2, 4096)]
        public int Width = DefaultWidth;

        /// <summary>
        /// The height of the texture in pixels
        /// </summary>
        [Range(2, 4096)]
        public int Height = DefaultHeight;

        /// <summary>
        /// The material to apply any streaming video to
        /// </summary>
        public Material TargetMaterial;

        /// <summary>
        /// 是否启用 Unity 音频输出。
        /// </summary>
        [Header("Audio Properties:")]
        public bool EnableAudio = true;

        /// <summary>
        /// 是否在缓冲足够后自动启动 Unity 音频播放。
        /// </summary>
        public bool AutoStartAudio = true;

        /// <summary>
        /// 实时流额外补偿的音频输出延迟。
        /// </summary>
        [Range(0, 500)]
        public int RealtimeAdditionalAudioSinkDelayMilliseconds = 60;

        /// <summary>
        /// 是否优先尝试 NativeVideo / 硬解增强路径。
        /// 失败时会自动回退到现有纹理上传路径。
        /// </summary>
        public bool PreferNativeVideo = true;

        /// <summary>
        /// 是否要求增强路径具备零 CPU 拷贝能力。
        /// </summary>
        public bool RequireNativeVideoZeroCopy;

        /// <summary>
        /// 是否要求增强路径启用硬件解码。
        /// </summary>
        public bool RequireNativeVideoHardwareDecode = true;

        /// <summary>
        /// 可选的 NativeVideo surface 覆盖。
        /// Unknown 表示按当前平台默认 surface 合同选择。
        /// 目前主要用于 iOS/Android bring-up 时显式固定目标合同。
        /// </summary>
        public NativeVideoSurfaceKind PreferredNativeVideoSurface = NativeVideoSurfaceKind.Unknown;

        /// <summary>
        /// 是否优先尝试 Unity 材质直接采样 source plane textures。
        /// 仅在 Windows NativeVideo + NV12/P010 source plane textures 可用时生效。
        /// </summary>
        public bool PreferNativeVideoUnityDirectShader = true;

        /// <summary>
        /// 是否优先尝试直接通过 RenderEvent 写入 Unity RenderTarget。
        /// 仅在 Windows NativeVideo + direct target present 可用时生效。
        /// </summary>
        public bool PreferNativeVideoRenderEventPass = true;

        /// <summary>
        /// 是否优先尝试 Unity Compute Shader 消费 source plane textures。
        /// 仅在 Windows NativeVideo + NV12/P010 source plane textures 可用时生效。
        /// </summary>
        public bool PreferNativeVideoUnityCompute;

        /// <summary>
        /// 可选的 NV12/P010 直接采样 Shader。为空时会尝试从 Resources/NV12Direct 加载。
        /// </summary>
        public Shader NativeVideoNv12DirectShader;

        /// <summary>
        /// 可选的 NV12/P010 -> RGBA Compute Shader。为空时会尝试从 Resources/NV12ToRGBA 加载。
        /// </summary>
        public ComputeShader NativeVideoNv12ComputeShader;

        private Texture _targetTexture;
        private Texture2D _boundNativeTexture;
        private Texture2D _nativePlaneTextureY;
        private Texture2D _nativePlaneTextureUV;
        private RenderTexture _nativeVideoComputeOutput;
        private int _id = InvalidPlayerId;
        private bool _playRequested;
        private bool _resumeAfterPause;
        private MediaBackendKind _actualBackendKind = MediaBackendKind.Auto;
        private bool _nativeVideoPathActive;
        private MediaNativeInteropCommon.NativeVideoInteropCapsView _nativeVideoInteropCaps;
        private float _playRequestedRealtimeAt = -1f;
        private float _firstNativeVideoFrameRealtimeAt = -1f;
        private NativeVideoFrameInfo _lastNativeVideoFrameInfo;
        private bool _hasLastNativeVideoFrameInfo;
        private long _lastAcquiredNativeFrameIndex = -1;
        private float _lastNativeVideoFrameAcquireRealtimeAt = -1f;
        private long _nativeVideoFrameAcquireAttemptCount;
        private long _nativeVideoFrameAcquireMissCount;
        private long _nativeVideoFrameAcquireCount;
        private long _nativeVideoFrameReleaseCount;
        private long _nativeVideoFrameDuplicateAcquireCount;
        private long _nativeVideoFrameConsecutiveDuplicateCount;
        private long _nativeVideoFrameMaxConsecutiveDuplicateCount;
        private long _nativeVideoFrameConsecutiveMissCount;
        private long _nativeVideoFrameMaxConsecutiveMissCount;
        private long _nativeVideoFramePresentedCount;
        private long _nativeVideoFramePresentedLifetimeCount;
        private long _nativeVideoFramePresentationFailureCount;
        private long _nativeVideoFrameRenderEventPassCount;
        private long _nativeVideoFrameDirectBindPresentCount;
        private long _nativeVideoFrameDirectShaderPresentCount;
        private long _nativeVideoFrameComputePresentCount;
        private long _nativeVideoFrameSkippedFrameCount;
        private long _nativeVideoFrameNonMonotonicCount;
        private long _nativeVideoFrameCadenceSampleCount;
        private long _nativeVideoFrameLastIndexDelta;
        private double _nativeVideoFrameLastTimeDeltaSec;
        private double _nativeVideoFrameLastRealtimeDeltaSec;
        private int _nativeVideoStartupWarmupStableFrameCount;
        private long _nativeVideoStartupWarmupSuppressedFrameCount;
        private bool _nativeVideoStartupWarmupCompleted;
        private double _nativeVideoFrameTimeDeltaSumSec;
        private double _nativeVideoFrameTimeDeltaMinSec = double.PositiveInfinity;
        private double _nativeVideoFrameTimeDeltaMaxSec;
        private double _nativeVideoFrameRealtimeDeltaSumSec;
        private double _nativeVideoFrameRealtimeDeltaMinSec = double.PositiveInfinity;
        private double _nativeVideoFrameRealtimeDeltaMaxSec;
        private long _nativeVideoUpdateCount;
        private float _lastNativeVideoUpdateRealtimeAt = -1f;
        private long _nativeVideoStartupUpdateLogCount;
        private double _nativeVideoUpdatePlayerElapsedMsSum;
        private double _nativeVideoUpdatePlayerElapsedMsMax;
        private double _nativeVideoUpdateNativeVideoFrameElapsedMsSum;
        private double _nativeVideoUpdateNativeVideoFrameElapsedMsMax;
        private double _nativeVideoUpdateAudioBufferElapsedMsSum;
        private double _nativeVideoUpdateAudioBufferElapsedMsMax;
        private long _nativeVideoRenderEventPassAttemptCount;
        private long _nativeVideoRenderEventPassSuccessCount;
        private long _nativeVideoDirectBindAttemptCount;
        private long _nativeVideoDirectBindSuccessCount;
        private long _nativeVideoDirectShaderAttemptCount;
        private long _nativeVideoDirectShaderSuccessCount;
        private long _nativeVideoDirectShaderSourcePlaneTexturesUnsupportedCount;
        private long _nativeVideoDirectShaderShaderUnavailableCount;
        private long _nativeVideoDirectShaderAcquireSourcePlaneTexturesFailureCount;
        private long _nativeVideoDirectShaderPlaneTexturesUsabilityFailureCount;
        private long _nativeVideoDirectShaderMaterialFailureCount;
        private long _nativeVideoDirectShaderExceptionCount;
        private long _nativeVideoComputeAttemptCount;
        private long _nativeVideoComputeSuccessCount;
        private long _nativeVideoComputeSourcePlaneTexturesUnsupportedCount;
        private long _nativeVideoComputeShaderUnavailableCount;
        private long _nativeVideoComputeAcquireSourcePlaneTexturesFailureCount;
        private long _nativeVideoComputePlaneTexturesUsabilityFailureCount;
        private long _nativeVideoComputeExceptionCount;
        private bool _nativeVideoBindingWarningIssued;
        private bool _nativeVideoDirectShaderWarningIssued;
        private bool _nativeVideoComputeWarningIssued;
        private bool _nativeTextureBound;
        private bool _nativePlaneTexturesBound;
        private bool _nativeVideoDirectShaderPathActive;
        private bool _nativeVideoComputePathActive;
        private bool _nativeVideoSourceSurfaceZeroCopyActive;
        private bool _nativeVideoSourcePlaneTexturesZeroCopyActive;
        private long _nativeTextureBindCount;
        private long _nativePlaneTextureBindCount;
        private long _nativeVideoDirectShaderBindCount;
        private NativeVideoPresentationPathKind _nativeVideoPresentationPath =
            NativeVideoPresentationPathKind.None;
        private NativeVideoActivationDecisionKind _nativeVideoActivationDecision =
            NativeVideoActivationDecisionKind.NotRequested;
        private IntPtr _lastBoundNativeHandle = IntPtr.Zero;
        private IntPtr _lastBoundNativePlaneYHandle = IntPtr.Zero;
        private IntPtr _lastBoundNativePlaneUVHandle = IntPtr.Zero;
        private NativeVideoPlaneTextureFormatKind _lastBoundNativePlaneYFormat =
            NativeVideoPlaneTextureFormatKind.Unknown;
        private NativeVideoPlaneTextureFormatKind _lastBoundNativePlaneUVFormat =
            NativeVideoPlaneTextureFormatKind.Unknown;
        private int _nativeVideoComputeKernel = -1;
        private Shader _originalTargetMaterialShader;
        private bool _capturedTargetMaterialShader;
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
        private int _nativeBufferedAudioBytes;
        private readonly object _audioLock = new object();


        private static readonly int UseNativeVideoPlaneTexturesPropertyId =
            Shader.PropertyToID("_UseNativeVideoPlaneTextures");
        private static readonly int YPlanePropertyId = Shader.PropertyToID("_YPlane");
        private static readonly int UVPlanePropertyId = Shader.PropertyToID("_UVPlane");
        private static readonly int FlipVerticalPropertyId = Shader.PropertyToID("_FlipVertical");
        private static readonly int VideoSourcePixelFormatPropertyId =
            Shader.PropertyToID("_VideoSourcePixelFormat");
        private static readonly int VideoColorRangePropertyId =
            Shader.PropertyToID("_VideoColorRange");
        private static readonly int VideoColorMatrixPropertyId =
            Shader.PropertyToID("_VideoColorMatrix");
        private static readonly int VideoColorPrimariesPropertyId =
            Shader.PropertyToID("_VideoColorPrimaries");
        private static readonly int VideoTransferPropertyId =
            Shader.PropertyToID("_VideoTransfer");
        private static readonly int VideoBitDepthPropertyId =
            Shader.PropertyToID("_VideoBitDepth");
        private static readonly int VideoDynamicRangePropertyId =
            Shader.PropertyToID("_VideoDynamicRange");
        public MediaBackendKind ActualBackendKind
        {
            get { return _actualBackendKind; }
        }

        public bool IsNativeVideoPathActive
        {
            get { return _nativeVideoPathActive; }
        }

        public bool HasPresentedNativeVideoFrame
        {
            get { return _lastAcquiredNativeFrameIndex >= 0; }
        }

        public long NativeVideoFrameAcquireCount
        {
            get { return _nativeVideoFrameAcquireCount; }
        }

        public long NativeVideoFrameReleaseCount
        {
            get { return _nativeVideoFrameReleaseCount; }
        }

        public bool IsNativeTextureBound
        {
            get { return _nativeTextureBound; }
        }

        public long NativeTextureBindCount
        {
            get { return _nativeTextureBindCount; }
        }

        public bool HasBoundNativeVideoPlaneTextures
        {
            get { return _nativePlaneTexturesBound; }
        }

        public bool IsNativeVideoComputePathActive
        {
            get { return _nativeVideoComputePathActive; }
        }

        public bool IsNativeVideoDirectShaderPathActive
        {
            get { return _nativeVideoDirectShaderPathActive; }
        }

        public long NativeVideoPlaneTextureBindCount
        {
            get { return _nativePlaneTextureBindCount; }
        }

        public long NativeVideoDirectShaderBindCount
        {
            get { return _nativeVideoDirectShaderBindCount; }
        }

        public NativeVideoPresentationPathKind NativeVideoPresentationPath
        {
            get { return _nativeVideoPresentationPath; }
        }

        public NativeVideoActivationDecisionKind NativeVideoActivationDecision
        {
            get { return _nativeVideoActivationDecision; }
        }

        public bool IsNativeVideoStrictZeroCopyActive
        {
            get
            {
                var presentedFrameStrictZeroCopy =
                    _hasLastNativeVideoFrameInfo
                    && (_nativeVideoPresentationPath == NativeVideoPresentationPathKind.DirectBind
                        || _nativeVideoPresentationPath
                            == NativeVideoPresentationPathKind.RenderEventPass)
                    && HasNativeVideoFrameFlag(
                        _lastNativeVideoFrameInfo.Flags,
                        MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy)
                    && !HasNativeVideoFrameFlag(
                        _lastNativeVideoFrameInfo.Flags,
                        MediaNativeInteropCommon.NativeVideoFrameFlagCpuFallback);

                var directShaderStrictZeroCopy =
                    _nativeVideoPresentationPath == NativeVideoPresentationPathKind.DirectShader
                    && _nativePlaneTexturesBound
                    && _nativeVideoDirectShaderPathActive
                    && !_nativeVideoComputePathActive
                    && _nativeVideoSourcePlaneTexturesZeroCopyActive;

                return presentedFrameStrictZeroCopy || directShaderStrictZeroCopy;
            }
        }

        public bool IsNativeVideoZeroCpuCopyActive
        {
            get
            {
                return _nativeVideoPathActive
                    && _nativeVideoActivationDecision == NativeVideoActivationDecisionKind.Active
                    && _nativeVideoSourceSurfaceZeroCopyActive;
            }
        }

        public bool IsNativeVideoSourcePlaneTexturesZeroCopyActive
        {
            get { return _nativeVideoSourcePlaneTexturesZeroCopyActive; }
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

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateTexture")]
        private static extern int GetPlayer(string uri, IntPtr texturePointer);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateTextureEx")]
        private static extern int GetPlayerEx(
            string uri,
            IntPtr texturePointer,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoInteropCaps")]
        private static extern int GetNativeVideoInteropCaps(
            int backendKind,
            string path,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget target,
            ref MediaNativeInteropCommon.RustAVNativeVideoInteropCaps caps);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateNativeVideoOutput")]
        private static extern int GetNativeVideoPlayer(
            string uri,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget target);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateNativeVideoOutputEx")]
        private static extern int GetNativeVideoPlayerEx(
            string uri,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget target,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerAcquireNativeVideoFrame")]
        private static extern int AcquireNativeVideoFrame(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoFrame frame);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerAcquireNativeVideoSourceFrame")]
        private static extern int AcquireNativeVideoSourceFrame(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoFrame frame);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoSourcePlaneTextures")]
        private static extern int GetNativeVideoSourcePlaneTextures(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoPlaneTextures textures);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoSourcePlaneViews")]
        private static extern int GetNativeVideoSourcePlaneViews(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoPlaneViews views);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoColorInfo")]
        private static extern int GetNativeVideoColorInfo(
            int id,
            ref MediaNativeInteropCommon.RustAVVideoColorInfo info);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoSourceColorInfo")]
        private static extern int GetNativeVideoSourceColorInfo(
            int id,
            ref MediaNativeInteropCommon.RustAVVideoColorInfo info);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerReleaseNativeVideoFrame")]
        private static extern int ReleaseNativeVideoFrame(
            int id,
            long frameIndex);

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

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetAudioMetaPCM")]
        private static extern int GetAudioMetaPCM(int id, out RustAVAudioMeta outMeta);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCopyAudioPCM")]
        private static extern int CopyAudioPCM(int id, byte[] destination, int destinationLength);

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

        /// <summary>
        /// Begins or resumes playback
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Play()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Play(_id);

            if (result < 0)
            {
                throw new Exception($"Failed to play with error {result}");
            }

            if (_playRequestedRealtimeAt < 0f)
            {
                _playRequestedRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
            }

            _playRequested = true;
            TryStartAudioSource();
            UpdateNativeAudioSinkDelay();
        }

        /// <summary>
        /// Stops playback
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public void Stop()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Stop(_id);

            if (result < 0)
            {
                throw new Exception($"Failed to stop with error {result}");
            }

            _playRequested = false;
            if (_audioSource != null)
            {
                _audioSource.Pause();
            }

            UpdateNativeAudioSinkDelay();
        }

        /// <summary>
        /// Evaluates the duration
        /// </summary>
        /// <returns>The duration in seconds</returns>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public double Duration()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Duration(_id);

            if (result < 0)
            {
                throw new Exception("Failed to get duration");
            }

            return result;
        }

        /// <summary>
        /// Evaluates the current time
        /// </summary>
        /// <returns>The time in seconds since playback start</returns>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public double Time()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Time(_id);

            if (result < 0)
            {
                throw new Exception("Failed to get time");
            }

            return result;
        }

        /// <summary>
        /// Seeks the playback
        /// </summary>
        /// <param name="time">The time to seek to in seconds</param>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public void Seek(double time)
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Seek(_id, time);

            if (result < 0)
            {
                throw new Exception($"Failed to seek with error {result}");
            }

            ClearAudioBuffer();
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            UpdateNativeAudioSinkDelay();
            TryStartAudioSource();
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
                SourceConnectionState = runtimeHealth.SourceConnectionState,
                IsConnected = runtimeHealth.IsConnected,
                IsPlaying = runtimeHealth.IsPlaying,
                IsRealtime = runtimeHealth.IsRealtime,
                SourcePacketCount = runtimeHealth.SourcePacketCount,
                SourceTimeoutCount = runtimeHealth.SourceTimeoutCount,
                SourceReconnectCount = runtimeHealth.SourceReconnectCount,
                SourceLastActivityAgeSec = runtimeHealth.SourceLastActivityAgeSec,
                CurrentTimeSec = runtimeHealth.CurrentTimeSec,
            };
            return true;
        }

        public bool TryGetNativeVideoInteropInfo(out NativeVideoInteropInfo info)
        {
            info = default(NativeVideoInteropInfo);
            if (!_nativeVideoInteropCaps.Supported && _nativeVideoInteropCaps.Flags == 0)
            {
                return false;
            }

            info = new NativeVideoInteropInfo
            {
                BackendKind = _nativeVideoInteropCaps.BackendKind,
                PlatformKind = _nativeVideoInteropCaps.PlatformKind,
                SurfaceKind = _nativeVideoInteropCaps.SurfaceKind,
                Supported = _nativeVideoInteropCaps.Supported,
                ContractTargetSupported = _nativeVideoInteropCaps.ContractTargetSupported,
                HardwareDecodeSupported = _nativeVideoInteropCaps.HardwareDecodeSupported,
                ZeroCopySupported = _nativeVideoInteropCaps.ZeroCopySupported,
                SourceSurfaceZeroCopySupported = _nativeVideoInteropCaps.SourceSurfaceZeroCopySupported,
                ExternalTextureTarget = _nativeVideoInteropCaps.ExternalTextureTarget,
                FallbackCopyPathSupported =
                    (_nativeVideoInteropCaps.Flags & MediaNativeInteropCommon.NativeVideoCapFlagFallbackCopyPath) != 0,
                PresentedFrameDirectBindable = _nativeVideoInteropCaps.PresentedFrameDirectBindable,
                PresentedFrameStrictZeroCopySupported = _nativeVideoInteropCaps.PresentedFrameStrictZeroCopySupported,
                SourcePlaneTexturesSupported = _nativeVideoInteropCaps.SourcePlaneTexturesSupported,
                SourcePlaneViewsSupported = _nativeVideoInteropCaps.SourcePlaneViewsSupported,
                AcquireReleaseSupported = _nativeVideoInteropCaps.AcquireReleaseSupported,
                RuntimeBridgePending = _nativeVideoInteropCaps.RuntimeBridgePending,
                Flags = _nativeVideoInteropCaps.Flags,
            };
            return true;
        }

        public bool TryAcquireNativeVideoFrameInfo(out NativeVideoFrameInfo frameInfo)
        {
            frameInfo = default(NativeVideoFrameInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoFrameView frameView;
            if (!MediaNativeInteropCommon.TryAcquireNativeVideoFrame(
                AcquireNativeVideoFrame,
                _id,
                out frameView))
            {
                return false;
            }

            frameInfo = new NativeVideoFrameInfo
            {
                SurfaceKind = frameView.SurfaceKind,
                PixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    frameView.PixelFormat),
                NativeHandle = frameView.NativeHandle,
                AuxiliaryHandle = frameView.AuxiliaryHandle,
                Width = frameView.Width,
                Height = frameView.Height,
                TimeSec = frameView.TimeSec,
                FrameIndex = frameView.FrameIndex,
                Flags = frameView.Flags,
            };
            return true;
        }

        public bool ReleaseNativeVideoFrameInfo(long frameIndex)
        {
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            return MediaNativeInteropCommon.TryReleaseNativeVideoFrame(
                ReleaseNativeVideoFrame,
                _id,
                frameIndex);
        }

        public bool TryAcquireNativeVideoSourceFrameInfo(out NativeVideoFrameInfo frameInfo)
        {
            frameInfo = default(NativeVideoFrameInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoFrameView frameView;
            if (!MediaNativeInteropCommon.TryAcquireNativeVideoFrame(
                AcquireNativeVideoSourceFrame,
                _id,
                out frameView))
            {
                return false;
            }

            frameInfo = new NativeVideoFrameInfo
            {
                SurfaceKind = frameView.SurfaceKind,
                PixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    frameView.PixelFormat),
                NativeHandle = frameView.NativeHandle,
                AuxiliaryHandle = frameView.AuxiliaryHandle,
                Width = frameView.Width,
                Height = frameView.Height,
                TimeSec = frameView.TimeSec,
                FrameIndex = frameView.FrameIndex,
                Flags = frameView.Flags,
            };
            _nativeVideoSourceSurfaceZeroCopyActive = HasNativeVideoFrameFlag(
                frameInfo.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            return true;
        }

        public bool TryGetNativeVideoColorInfo(out NativeVideoColorInfo colorInfo)
        {
            colorInfo = default(NativeVideoColorInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.VideoColorInfoView colorView;
            if (!MediaNativeInteropCommon.TryReadVideoColorInfo(
                GetNativeVideoColorInfo,
                _id,
                out colorView))
            {
                return false;
            }

            colorInfo = new NativeVideoColorInfo
            {
                Range = MediaNativeInteropCommon.ToPublicNativeVideoColorRange(colorView.Range),
                Matrix = MediaNativeInteropCommon.ToPublicNativeVideoColorMatrix(colorView.Matrix),
                Primaries = MediaNativeInteropCommon.ToPublicNativeVideoColorPrimaries(
                    colorView.Primaries),
                Transfer =
                    MediaNativeInteropCommon.ToPublicNativeVideoTransferCharacteristic(
                        colorView.Transfer),
                BitDepth = colorView.BitDepth,
                DynamicRange =
                    MediaNativeInteropCommon.ToPublicNativeVideoDynamicRange(
                        colorView.DynamicRange),
            };
            return true;
        }

        public bool TryGetNativeVideoSourceColorInfo(out NativeVideoColorInfo colorInfo)
        {
            colorInfo = default(NativeVideoColorInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.VideoColorInfoView colorView;
            if (!MediaNativeInteropCommon.TryReadVideoColorInfo(
                GetNativeVideoSourceColorInfo,
                _id,
                out colorView))
            {
                return false;
            }

            colorInfo = new NativeVideoColorInfo
            {
                Range = MediaNativeInteropCommon.ToPublicNativeVideoColorRange(colorView.Range),
                Matrix = MediaNativeInteropCommon.ToPublicNativeVideoColorMatrix(colorView.Matrix),
                Primaries = MediaNativeInteropCommon.ToPublicNativeVideoColorPrimaries(
                    colorView.Primaries),
                Transfer =
                    MediaNativeInteropCommon.ToPublicNativeVideoTransferCharacteristic(
                        colorView.Transfer),
                BitDepth = colorView.BitDepth,
                DynamicRange =
                    MediaNativeInteropCommon.ToPublicNativeVideoDynamicRange(
                        colorView.DynamicRange),
            };
            return true;
        }

        public bool TryAcquireNativeVideoSourcePlaneTexturesInfo(
            out NativeVideoPlaneTexturesInfo texturesInfo)
        {
            texturesInfo = default(NativeVideoPlaneTexturesInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoPlaneTexturesView texturesView;
            if (!MediaNativeInteropCommon.TryReadNativeVideoSourcePlaneTextures(
                GetNativeVideoSourcePlaneTextures,
                _id,
                out texturesView))
            {
                return false;
            }

            texturesInfo = new NativeVideoPlaneTexturesInfo
            {
                SurfaceKind = texturesView.SurfaceKind,
                SourcePixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    texturesView.SourcePixelFormat),
                YNativeHandle = texturesView.YNativeHandle,
                YAuxiliaryHandle = texturesView.YAuxiliaryHandle,
                YWidth = texturesView.YWidth,
                YHeight = texturesView.YHeight,
                YTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    texturesView.YTextureFormat),
                UVNativeHandle = texturesView.UVNativeHandle,
                UVAuxiliaryHandle = texturesView.UVAuxiliaryHandle,
                UVWidth = texturesView.UVWidth,
                UVHeight = texturesView.UVHeight,
                UVTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    texturesView.UVTextureFormat),
                TimeSec = texturesView.TimeSec,
                FrameIndex = texturesView.FrameIndex,
                Flags = texturesView.Flags,
            };
            _nativeVideoSourcePlaneTexturesZeroCopyActive = HasNativeVideoFrameFlag(
                texturesInfo.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            return true;
        }

        public bool TryAcquireNativeVideoSourcePlaneViewsInfo(
            out NativeVideoPlaneViewsInfo viewsInfo)
        {
            viewsInfo = default(NativeVideoPlaneViewsInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoPlaneViewsView viewsView;
            if (!MediaNativeInteropCommon.TryReadNativeVideoSourcePlaneViews(
                GetNativeVideoSourcePlaneViews,
                _id,
                out viewsView))
            {
                return false;
            }

            viewsInfo = new NativeVideoPlaneViewsInfo
            {
                SurfaceKind = viewsView.SurfaceKind,
                SourcePixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    viewsView.SourcePixelFormat),
                YNativeHandle = viewsView.YNativeHandle,
                YAuxiliaryHandle = viewsView.YAuxiliaryHandle,
                YWidth = viewsView.YWidth,
                YHeight = viewsView.YHeight,
                YTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    viewsView.YTextureFormat),
                YResourceKind = MediaNativeInteropCommon.ToPublicNativeVideoPlaneResourceKind(
                    viewsView.YResourceKind),
                UVNativeHandle = viewsView.UVNativeHandle,
                UVAuxiliaryHandle = viewsView.UVAuxiliaryHandle,
                UVWidth = viewsView.UVWidth,
                UVHeight = viewsView.UVHeight,
                UVTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    viewsView.UVTextureFormat),
                UVResourceKind = MediaNativeInteropCommon.ToPublicNativeVideoPlaneResourceKind(
                    viewsView.UVResourceKind),
                TimeSec = viewsView.TimeSec,
                FrameIndex = viewsView.FrameIndex,
                Flags = viewsView.Flags,
            };
            return true;
        }

        public bool TryGetPresentedNativeVideoTimeSec(out double presentedVideoTimeSec)
        {
            presentedVideoTimeSec = _hasLastNativeVideoFrameInfo
                ? _lastNativeVideoFrameInfo.TimeSec
                : -1.0;
            return presentedVideoTimeSec >= 0.0;
        }

        public bool TryGetLastNativeVideoFrameInfo(out NativeVideoFrameInfo frameInfo)
        {
            frameInfo = _lastNativeVideoFrameInfo;
            return _hasLastNativeVideoFrameInfo;
        }

        public bool TryTakeNativeVideoFrameCadenceSnapshot(
            out NativeVideoFrameCadenceSnapshot snapshot)
        {
            snapshot = default(NativeVideoFrameCadenceSnapshot);
            if (_nativeVideoFrameAcquireAttemptCount <= 0
                && _nativeVideoFrameAcquireMissCount <= 0
                && _nativeVideoFramePresentedCount <= 0
                && _nativeVideoFrameCadenceSampleCount <= 0
                && _nativeVideoFrameDuplicateAcquireCount <= 0
                && _nativeVideoFrameSkippedFrameCount <= 0
                && _nativeVideoFrameNonMonotonicCount <= 0
                && _nativeVideoFramePresentationFailureCount <= 0
                && _nativeVideoFrameRenderEventPassCount <= 0
                && _nativeVideoFrameDirectBindPresentCount <= 0
                && _nativeVideoFrameDirectShaderPresentCount <= 0
                && _nativeVideoFrameComputePresentCount <= 0)
            {
                return false;
            }

            snapshot = new NativeVideoFrameCadenceSnapshot
            {
                AcquireAttemptCount = _nativeVideoFrameAcquireAttemptCount,
                AcquireMissCount = _nativeVideoFrameAcquireMissCount,
                PresentCount = _nativeVideoFramePresentedCount,
                SampleCount = _nativeVideoFrameCadenceSampleCount,
                DuplicateCount = _nativeVideoFrameDuplicateAcquireCount,
                MaxDuplicateStreak = _nativeVideoFrameMaxConsecutiveDuplicateCount,
                MaxAcquireMissStreak = _nativeVideoFrameMaxConsecutiveMissCount,
                SkippedFrameCount = _nativeVideoFrameSkippedFrameCount,
                NonMonotonicCount = _nativeVideoFrameNonMonotonicCount,
                LastFrameIndexDelta = _nativeVideoFrameLastIndexDelta,
                LastFrameTimeDeltaSec = _nativeVideoFrameLastTimeDeltaSec,
                LastRealtimeDeltaSec = _nativeVideoFrameLastRealtimeDeltaSec,
                MinFrameTimeDeltaSec = double.IsPositiveInfinity(_nativeVideoFrameTimeDeltaMinSec)
                    ? 0.0
                    : _nativeVideoFrameTimeDeltaMinSec,
                MaxFrameTimeDeltaSec = _nativeVideoFrameTimeDeltaMaxSec,
                AvgFrameTimeDeltaSec = _nativeVideoFrameCadenceSampleCount > 0
                    ? _nativeVideoFrameTimeDeltaSumSec / _nativeVideoFrameCadenceSampleCount
                    : 0.0,
                MinRealtimeDeltaSec = double.IsPositiveInfinity(_nativeVideoFrameRealtimeDeltaMinSec)
                    ? 0.0
                    : _nativeVideoFrameRealtimeDeltaMinSec,
                MaxRealtimeDeltaSec = _nativeVideoFrameRealtimeDeltaMaxSec,
                AvgRealtimeDeltaSec = _nativeVideoFrameCadenceSampleCount > 0
                    ? _nativeVideoFrameRealtimeDeltaSumSec / _nativeVideoFrameCadenceSampleCount
                    : 0.0,
                PresentationFailureCount = _nativeVideoFramePresentationFailureCount,
                RenderEventPassCount = _nativeVideoFrameRenderEventPassCount,
                DirectBindCount = _nativeVideoFrameDirectBindPresentCount,
                DirectShaderCount = _nativeVideoFrameDirectShaderPresentCount,
                ComputeCount = _nativeVideoFrameComputePresentCount
            };

            ResetNativeVideoFrameCadenceStats();
            return true;
        }

        public bool TryTakeNativeVideoUpdateTimingSnapshot(
            out NativeVideoUpdateTimingSnapshot snapshot)
        {
            snapshot = default(NativeVideoUpdateTimingSnapshot);
            if (_nativeVideoUpdateCount <= 0
                && _nativeVideoUpdatePlayerElapsedMsSum <= 0.0
                && _nativeVideoUpdateNativeVideoFrameElapsedMsSum <= 0.0
                && _nativeVideoUpdateAudioBufferElapsedMsSum <= 0.0)
            {
                return false;
            }

            snapshot = new NativeVideoUpdateTimingSnapshot
            {
                UpdateCount = _nativeVideoUpdateCount,
                UpdatePlayerElapsedMsAvg = _nativeVideoUpdateCount > 0
                    ? _nativeVideoUpdatePlayerElapsedMsSum / _nativeVideoUpdateCount
                    : 0.0,
                UpdatePlayerElapsedMsMax = _nativeVideoUpdatePlayerElapsedMsMax,
                UpdateNativeVideoFrameElapsedMsAvg = _nativeVideoUpdateCount > 0
                    ? _nativeVideoUpdateNativeVideoFrameElapsedMsSum / _nativeVideoUpdateCount
                    : 0.0,
                UpdateNativeVideoFrameElapsedMsMax = _nativeVideoUpdateNativeVideoFrameElapsedMsMax,
                UpdateAudioBufferElapsedMsAvg = _nativeVideoUpdateCount > 0
                    ? _nativeVideoUpdateAudioBufferElapsedMsSum / _nativeVideoUpdateCount
                    : 0.0,
                UpdateAudioBufferElapsedMsMax = _nativeVideoUpdateAudioBufferElapsedMsMax,
            };

            ResetNativeVideoUpdateTimingStats();
            return true;
        }

        public bool TryTakeNativeVideoPresentationTelemetrySnapshot(
            out NativeVideoPresentationTelemetrySnapshot snapshot)
        {
            snapshot = default(NativeVideoPresentationTelemetrySnapshot);
            if (_nativeVideoRenderEventPassAttemptCount <= 0
                && _nativeVideoRenderEventPassSuccessCount <= 0
                && _nativeVideoDirectBindAttemptCount <= 0
                && _nativeVideoDirectBindSuccessCount <= 0
                && _nativeVideoDirectShaderAttemptCount <= 0
                && _nativeVideoDirectShaderSuccessCount <= 0
                && _nativeVideoDirectShaderSourcePlaneTexturesUnsupportedCount <= 0
                && _nativeVideoDirectShaderShaderUnavailableCount <= 0
                && _nativeVideoDirectShaderAcquireSourcePlaneTexturesFailureCount <= 0
                && _nativeVideoDirectShaderPlaneTexturesUsabilityFailureCount <= 0
                && _nativeVideoDirectShaderMaterialFailureCount <= 0
                && _nativeVideoDirectShaderExceptionCount <= 0
                && _nativeVideoComputeAttemptCount <= 0
                && _nativeVideoComputeSuccessCount <= 0
                && _nativeVideoComputeSourcePlaneTexturesUnsupportedCount <= 0
                && _nativeVideoComputeShaderUnavailableCount <= 0
                && _nativeVideoComputeAcquireSourcePlaneTexturesFailureCount <= 0
                && _nativeVideoComputePlaneTexturesUsabilityFailureCount <= 0
                && _nativeVideoComputeExceptionCount <= 0)
            {
                return false;
            }

            snapshot = new NativeVideoPresentationTelemetrySnapshot
            {
                RenderEventPassAttemptCount = _nativeVideoRenderEventPassAttemptCount,
                RenderEventPassSuccessCount = _nativeVideoRenderEventPassSuccessCount,
                DirectBindAttemptCount = _nativeVideoDirectBindAttemptCount,
                DirectBindSuccessCount = _nativeVideoDirectBindSuccessCount,
                DirectShaderAttemptCount = _nativeVideoDirectShaderAttemptCount,
                DirectShaderSuccessCount = _nativeVideoDirectShaderSuccessCount,
                DirectShaderSourcePlaneTexturesUnsupportedCount =
                    _nativeVideoDirectShaderSourcePlaneTexturesUnsupportedCount,
                DirectShaderShaderUnavailableCount =
                    _nativeVideoDirectShaderShaderUnavailableCount,
                DirectShaderAcquireSourcePlaneTexturesFailureCount =
                    _nativeVideoDirectShaderAcquireSourcePlaneTexturesFailureCount,
                DirectShaderPlaneTexturesUsabilityFailureCount =
                    _nativeVideoDirectShaderPlaneTexturesUsabilityFailureCount,
                DirectShaderMaterialFailureCount =
                    _nativeVideoDirectShaderMaterialFailureCount,
                DirectShaderExceptionCount = _nativeVideoDirectShaderExceptionCount,
                ComputeAttemptCount = _nativeVideoComputeAttemptCount,
                ComputeSuccessCount = _nativeVideoComputeSuccessCount,
                ComputeSourcePlaneTexturesUnsupportedCount =
                    _nativeVideoComputeSourcePlaneTexturesUnsupportedCount,
                ComputeShaderUnavailableCount = _nativeVideoComputeShaderUnavailableCount,
                ComputeAcquireSourcePlaneTexturesFailureCount =
                    _nativeVideoComputeAcquireSourcePlaneTexturesFailureCount,
                ComputePlaneTexturesUsabilityFailureCount =
                    _nativeVideoComputePlaneTexturesUsabilityFailureCount,
                ComputeExceptionCount = _nativeVideoComputeExceptionCount,
            };

            ResetNativeVideoPresentationTelemetryStats();
            return true;
        }

        private IEnumerator Start()
        {
            NativeInitializer.Initialize(this);
            Debug.Log(
                "[MediaPlayer] start_prepare_playable_source"
                + " uri=" + Uri
                + " backend=" + PreferredBackend);

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

            _isRealtimeSource = preparedSource != null && preparedSource.IsRealtimeSource;
            if (EnableAudio)
            {
                EnsureAudioSource();
            }

            Debug.Log(
                "[MediaPlayer] prepared_playable_source"
                + " playback_uri=" + (preparedSource != null ? preparedSource.PlaybackUri : "null")
                + " backend=" + PreferredBackend);
            InitializeNativePlayer(preparedSource);
        }

        private void Update()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            _nativeVideoUpdateCount += 1;
            var updateRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
            var updateDeltaSec = _lastNativeVideoUpdateRealtimeAt >= 0f
                ? updateRealtimeAt - _lastNativeVideoUpdateRealtimeAt
                : 0f;
            _lastNativeVideoUpdateRealtimeAt = updateRealtimeAt;

            var updatePlayerStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdatePlayer(_id);
            var updatePlayerElapsedMs = ElapsedMilliseconds(updatePlayerStartTicks);
            _nativeVideoUpdatePlayerElapsedMsSum += updatePlayerElapsedMs;
            _nativeVideoUpdatePlayerElapsedMsMax = Math.Max(
                _nativeVideoUpdatePlayerElapsedMsMax,
                updatePlayerElapsedMs);

            var updateNativeVideoStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdateNativeVideoFrame();
            var updateNativeVideoElapsedMs = ElapsedMilliseconds(updateNativeVideoStartTicks);
            _nativeVideoUpdateNativeVideoFrameElapsedMsSum += updateNativeVideoElapsedMs;
            _nativeVideoUpdateNativeVideoFrameElapsedMsMax = Math.Max(
                _nativeVideoUpdateNativeVideoFrameElapsedMsMax,
                updateNativeVideoElapsedMs);

            var updateAudioStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdateAudioBuffer();
            var updateAudioElapsedMs = ElapsedMilliseconds(updateAudioStartTicks);
            _nativeVideoUpdateAudioBufferElapsedMsSum += updateAudioElapsedMs;
            _nativeVideoUpdateAudioBufferElapsedMsMax = Math.Max(
                _nativeVideoUpdateAudioBufferElapsedMsMax,
                updateAudioElapsedMs);
            UpdateNativeAudioSinkDelay();
            MaybeLogNativeVideoStartupUpdate(
                updateDeltaSec,
                updatePlayerElapsedMs,
                updateNativeVideoElapsedMs,
                updateAudioElapsedMs);
        }

        private void MaybeLogNativeVideoStartupUpdate(
            float updateDeltaSec,
            double updatePlayerElapsedMs,
            double updateNativeVideoElapsedMs,
            double updateAudioElapsedMs)
        {
            if (!TraceNativeVideoCadence || !_nativeVideoPathActive)
            {
                return;
            }

            var startupSeconds = StartupElapsedSeconds;
            var shouldLog = startupSeconds <= 1.0f
                || updateDeltaSec >= 0.030f
                || _nativeVideoFrameConsecutiveDuplicateCount > 0
                || _nativeVideoFrameConsecutiveMissCount > 0;
            if (!shouldLog)
            {
                return;
            }

            _nativeVideoStartupUpdateLogCount += 1;
            if (startupSeconds > 1.0f
                && _nativeVideoStartupUpdateLogCount > 150
                && updateDeltaSec < 0.030f
                && _nativeVideoFrameConsecutiveDuplicateCount == 0
                && _nativeVideoFrameConsecutiveMissCount == 0)
            {
                return;
            }

            var bufferedSamples = 0;
            var ringCapacity = 0;
            lock (_audioLock)
            {
                bufferedSamples = _audioBufferedSamples;
                ringCapacity = _audioRing != null ? _audioRing.Length : 0;
            }

            Debug.Log(
                "[MediaPlayer] native_video_update_tick"
                + " startup_seconds=" + startupSeconds.ToString("F3")
                + " update_log_index=" + _nativeVideoStartupUpdateLogCount
                + " update_delta_ms=" + (updateDeltaSec * 1000f).ToString("F1")
                + " unity_delta_ms=" + (UnityEngine.Time.deltaTime * 1000f).ToString("F1")
                + " unity_unscaled_delta_ms=" + (UnityEngine.Time.unscaledDeltaTime * 1000f).ToString("F1")
                + " update_player_ms=" + updatePlayerElapsedMs.ToString("F2")
                + " update_video_ms=" + updateNativeVideoElapsedMs.ToString("F2")
                + " update_audio_ms=" + updateAudioElapsedMs.ToString("F2")
                + " acquire_attempts=" + _nativeVideoFrameAcquireAttemptCount
                + " acquire_misses=" + _nativeVideoFrameAcquireMissCount
                + " duplicates=" + _nativeVideoFrameDuplicateAcquireCount
                + " duplicate_streak=" + _nativeVideoFrameConsecutiveDuplicateCount
                + " miss_streak=" + _nativeVideoFrameConsecutiveMissCount
                + " presented_total=" + _nativeVideoFramePresentedCount
                + " last_frame_index=" + _lastAcquiredNativeFrameIndex
                + " last_frame_time=" + (_hasLastNativeVideoFrameInfo
                    ? _lastNativeVideoFrameInfo.TimeSec.ToString("F3")
                    : "NA")
                + " audio_buffered_samples=" + bufferedSamples
                + " audio_ring_capacity=" + ringCapacity
                + " audio_playing=" + (_audioSource != null && _audioSource.isPlaying)
                + " has_presented_frame=" + HasPresentedNativeVideoFrame
                + " presentation_path=" + _nativeVideoPresentationPath);
        }

        private void InitializeNativePlayer(MediaSourceResolver.PreparedMediaSource preparedSource)
        {
            Debug.Log(
                "[MediaPlayer] initialize_native_player begin"
                + " prepared_uri=" + (preparedSource != null ? preparedSource.PlaybackUri : "null")
                + " backend=" + PreferredBackend);
            var uri = preparedSource.PlaybackUri;
            try
            {
                _targetTexture = CreateNativeVideoTargetTexture();
                var targetHandle = _targetTexture != null
                    ? _targetTexture.GetNativeTexturePtr()
                    : IntPtr.Zero;
                var auxiliaryHandle = ResolveNativeVideoAuxiliaryHandle(_targetTexture);
                var targetPlatformKind = MediaNativeInteropCommon.DetectNativeVideoPlatformKind();
                var resolvedNativeSurfaceKind =
                    MediaNativeInteropCommon.ResolvePreferredNativeVideoSurfaceKind(
                        targetPlatformKind,
                        PreferredNativeVideoSurface);
                var targetProvider = MediaNativeInteropCommon.DescribeNativeVideoTargetProvider(
                    targetPlatformKind,
                    resolvedNativeSurfaceKind);
                Debug.Log(
                    "[MediaPlayer] create_target_texture"
                    + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null")
                    + " target_handle=0x" + targetHandle.ToInt64().ToString("X")
                    + " auxiliary_handle=0x" + auxiliaryHandle.ToInt64().ToString("X")
                    + " graphics_format=" + DescribeTextureGraphicsFormat(_targetTexture)
                    + " msaa=" + DescribeTextureMsaa(_targetTexture)
                    + " use_mip_map=" + DescribeTextureUseMipMap(_targetTexture)
                    + " random_write=" + DescribeTextureRandomWrite(_targetTexture)
                    + " target_provider=" + targetProvider
                    + " surface_selection="
                    + MediaNativeInteropCommon.DescribeNativeVideoSurfaceSelection(
                        targetPlatformKind,
                        PreferredNativeVideoSurface,
                        resolvedNativeSurfaceKind)
                    + " size=" + Width + "x" + Height);

                var openOptions = MediaNativeInteropCommon.CreateOpenOptions(
                    PreferredBackend,
                    StrictBackend);
                var nativeTargetExtraFlags = ResolveNativeVideoTargetExtraFlags();
                var nativeTarget = MediaNativeInteropCommon.CreateNativeVideoTarget(
                    targetPlatformKind,
                    resolvedNativeSurfaceKind,
                    targetHandle,
                    auxiliaryHandle,
                    Width,
                    Height,
                    nativeTargetExtraFlags);
                _nativeVideoInteropCaps = default(MediaNativeInteropCommon.NativeVideoInteropCapsView);
                _nativeVideoPathActive = false;
                _nativeVideoActivationDecision = PreferNativeVideo
                    ? NativeVideoActivationDecisionKind.CreateFailed
                    : NativeVideoActivationDecisionKind.NotRequested;

                if (PreferNativeVideo)
                {
                    TryCreateNativeVideoPlayer(uri, ref nativeTarget, ref openOptions);
                }

                if (!ValidatePlayerId(_id))
                {
                    Debug.Log(
                        "[MediaPlayer] fallback_texture_player_create"
                        + " target_handle=0x" + targetHandle.ToInt64().ToString("X")
                        + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null"));
                    try
                    {
                        _id = GetPlayerEx(uri, targetHandle, ref openOptions);
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _id = GetPlayer(uri, targetHandle);
                    }
                }

                if (ValidatePlayerId(_id))
                {
                    NativeInitializer.RegisterPlayerRenderEvent(_id);
                    _actualBackendKind = ReadActualBackendKind();
                    Debug.Log(
                        "[MediaPlayer] player_created requested_backend=" + PreferredBackend
                        + " actual_backend=" + _actualBackendKind
                        + " strict_backend=" + StrictBackend
                        + " native_video_requested=" + PreferNativeVideo
                        + " native_video_active=" + _nativeVideoPathActive
                        + " native_video_activation_decision=" + _nativeVideoActivationDecision
                        + " requested_surface=" + PreferredNativeVideoSurface
                        + " target_platform=" + (NativeVideoPlatformKind)nativeTarget.PlatformKind
                        + " target_surface=" + (NativeVideoSurfaceKind)nativeTarget.SurfaceKind
                        + " target_provider="
                        + MediaNativeInteropCommon.DescribeNativeVideoTargetProvider(
                            (NativeVideoPlatformKind)nativeTarget.PlatformKind,
                            (NativeVideoSurfaceKind)nativeTarget.SurfaceKind)
                        + " target_contract="
                        + MediaNativeInteropCommon.DescribeNativeVideoTargetContract(
                            (NativeVideoPlatformKind)nativeTarget.PlatformKind,
                            (NativeVideoSurfaceKind)nativeTarget.SurfaceKind)
                        + " caps_supported=" + _nativeVideoInteropCaps.Supported
                        + " contract_target_supported="
                        + _nativeVideoInteropCaps.ContractTargetSupported
                        + " runtime_bridge_pending="
                        + _nativeVideoInteropCaps.RuntimeBridgePending
                        + " unity_direct_shader_requested="
                        + PreferNativeVideoUnityDirectShader
                        + " unity_compute_requested=" + PreferNativeVideoUnityCompute
                        + " source_plane_textures_supported="
                        + _nativeVideoInteropCaps.SourcePlaneTexturesSupported);
                    ApplyPresentedTexture(_targetTexture);
                    SetLoop(_id, Loop);
                    UpdateNativeAudioSinkDelay();
                }
                else
                {
                    var diagnostic = ReadBackendRuntimeDiagnostic(uri);
                    if (string.IsNullOrEmpty(diagnostic))
                    {
                        throw new Exception($"Failed to create player with error: {_id}");
                    }

                    throw new Exception($"Failed to create player with error: {_id}; {diagnostic}");
                }

                if (AutoPlay)
                {
                    Play();
                }
            }
            catch
            {
                ReleaseNativePlayerSilently();
                ReleaseManagedResources();
                throw;
            }
        }

        private void UpdateNativeVideoFrame()
        {
            if (!_nativeVideoPathActive)
            {
                return;
            }

            _nativeVideoFrameAcquireAttemptCount += 1;

            NativeVideoFrameInfo frameInfo;
            if (!TryAcquireNativeVideoFrameInfo(out frameInfo))
            {
                _nativeVideoFrameAcquireMissCount += 1;
                _nativeVideoFrameConsecutiveMissCount += 1;
                if (ShouldWarmupFileNativeVideoStartupPresentation())
                {
                    _nativeVideoStartupWarmupStableFrameCount = 0;
                }
                _nativeVideoFrameMaxConsecutiveMissCount = Math.Max(
                    _nativeVideoFrameMaxConsecutiveMissCount,
                    _nativeVideoFrameConsecutiveMissCount);
                if (TraceNativeVideoCadence
                    && (_nativeVideoFrameConsecutiveMissCount == 1
                        || _nativeVideoFrameConsecutiveMissCount % 15 == 0))
                {
                    Debug.Log(
                        "[MediaPlayer] native_video_frame_acquire_miss"
                        + " startup_seconds=" + StartupElapsedSeconds.ToString("F3")
                        + " acquire_attempts=" + _nativeVideoFrameAcquireAttemptCount
                        + " acquire_misses=" + _nativeVideoFrameAcquireMissCount
                        + " miss_streak=" + _nativeVideoFrameConsecutiveMissCount
                        + " presentation_path=" + _nativeVideoPresentationPath);
                }
                return;
            }

            _nativeVideoFrameConsecutiveMissCount = 0;
            var now = UnityEngine.Time.realtimeSinceStartup;
            try
            {
                if (frameInfo.FrameIndex == _lastAcquiredNativeFrameIndex)
                {
                    _nativeVideoFrameDuplicateAcquireCount += 1;
                    _nativeVideoFrameConsecutiveDuplicateCount += 1;
                    if (ShouldWarmupFileNativeVideoStartupPresentation())
                    {
                        _nativeVideoStartupWarmupStableFrameCount = 0;
                    }
                    _nativeVideoFrameMaxConsecutiveDuplicateCount = Math.Max(
                        _nativeVideoFrameMaxConsecutiveDuplicateCount,
                        _nativeVideoFrameConsecutiveDuplicateCount);
                    if (TraceNativeVideoCadence)
                    {
                        Debug.Log(
                            "[MediaPlayer] native_video_frame_duplicate"
                            + " startup_seconds=" + StartupElapsedSeconds.ToString("F3")
                            + " frame_index=" + frameInfo.FrameIndex
                            + " frame_time=" + frameInfo.TimeSec.ToString("F3")
                            + " duplicates=" + _nativeVideoFrameDuplicateAcquireCount
                            + " duplicate_streak=" + _nativeVideoFrameConsecutiveDuplicateCount
                            + " acquire_attempts=" + _nativeVideoFrameAcquireAttemptCount
                            + " acquire_misses=" + _nativeVideoFrameAcquireMissCount
                            + " presentation_path=" + _nativeVideoPresentationPath);
                    }
                    return;
                }

                _nativeVideoFrameConsecutiveDuplicateCount = 0;
                var hasLastFrame = _hasLastNativeVideoFrameInfo;
                var frameIndexDelta = hasLastFrame
                    ? frameInfo.FrameIndex - _lastAcquiredNativeFrameIndex
                    : 0;
                var frameTimeDeltaSec = hasLastFrame
                    ? frameInfo.TimeSec - _lastNativeVideoFrameInfo.TimeSec
                    : 0.0;
                var realtimeDeltaSec = _lastNativeVideoFrameAcquireRealtimeAt >= 0f
                    ? now - _lastNativeVideoFrameAcquireRealtimeAt
                    : 0.0f;

                _lastAcquiredNativeFrameIndex = frameInfo.FrameIndex;
                _lastNativeVideoFrameInfo = frameInfo;
                _hasLastNativeVideoFrameInfo = true;
                _nativeVideoFrameAcquireCount += 1;
                _lastNativeVideoFrameAcquireRealtimeAt = now;

                if (hasLastFrame)
                {
                    _nativeVideoFrameCadenceSampleCount += 1;
                    _nativeVideoFrameLastIndexDelta = frameIndexDelta;
                    _nativeVideoFrameLastTimeDeltaSec = frameTimeDeltaSec;
                    _nativeVideoFrameLastRealtimeDeltaSec = realtimeDeltaSec;

                    if (frameIndexDelta < 0)
                    {
                        _nativeVideoFrameNonMonotonicCount += 1;
                    }
                    else if (frameIndexDelta > 1)
                    {
                        _nativeVideoFrameSkippedFrameCount += frameIndexDelta - 1;
                    }

                    if (frameTimeDeltaSec <= 0.0)
                    {
                        _nativeVideoFrameNonMonotonicCount += 1;
                    }
                    else
                    {
                        _nativeVideoFrameTimeDeltaSumSec += frameTimeDeltaSec;
                        _nativeVideoFrameTimeDeltaMinSec =
                            Math.Min(_nativeVideoFrameTimeDeltaMinSec, frameTimeDeltaSec);
                        _nativeVideoFrameTimeDeltaMaxSec =
                            Math.Max(_nativeVideoFrameTimeDeltaMaxSec, frameTimeDeltaSec);
                    }

                    if (realtimeDeltaSec > 0.0)
                    {
                        _nativeVideoFrameRealtimeDeltaSumSec += realtimeDeltaSec;
                        _nativeVideoFrameRealtimeDeltaMinSec =
                            Math.Min(_nativeVideoFrameRealtimeDeltaMinSec, realtimeDeltaSec);
                        _nativeVideoFrameRealtimeDeltaMaxSec =
                            Math.Max(_nativeVideoFrameRealtimeDeltaMaxSec, realtimeDeltaSec);
                    }
                }

                if (_firstNativeVideoFrameRealtimeAt < 0f)
                {
                    _firstNativeVideoFrameRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
                    NativeVideoColorInfo colorInfo;
                    var hasColorInfo = TryGetNativeVideoColorInfo(out colorInfo);
                    Debug.Log(
                        "[MediaPlayer] first_native_video_frame startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_time=" + frameInfo.TimeSec.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " surface=" + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " flags=0x" + frameInfo.Flags.ToString("X")
                        + (hasColorInfo
                            ? " color_info=" + DescribeNativeVideoColorInfo(colorInfo)
                            : string.Empty));
                }

                if (TraceNativeVideoCadence && StartupElapsedSeconds <= 0.200f)
                {
                    Debug.Log(
                        "[MediaPlayer] native_video_warmup_eval"
                        + " startup_seconds=" + StartupElapsedSeconds.ToString("F3")
                        + " is_realtime=" + _isRealtimeSource
                        + " native_active=" + _nativeVideoPathActive
                        + " external_texture_target=" + _nativeVideoInteropCaps.ExternalTextureTarget
                        + " presented_total=" + _nativeVideoFramePresentedCount
                        + " presented_lifetime_total=" + _nativeVideoFramePresentedLifetimeCount
                        + " warmup_completed=" + _nativeVideoStartupWarmupCompleted
                        + " has_last_frame=" + hasLastFrame
                        + " frame_index=" + frameInfo.FrameIndex
                        + " frame_index_delta=" + frameIndexDelta
                        + " frame_time_delta_ms=" + (frameTimeDeltaSec * 1000.0).ToString("F1")
                        + " realtime_delta_ms=" + (realtimeDeltaSec * 1000.0).ToString("F1"));
                }

                if (ShouldHoldFileNativeVideoStartupPresentation(
                    hasLastFrame,
                    frameIndexDelta,
                    frameTimeDeltaSec,
                    realtimeDeltaSec))
                {
                    _nativeVideoStartupWarmupSuppressedFrameCount += 1;
                    if (TraceNativeVideoCadence)
                    {
                        Debug.Log(
                            "[MediaPlayer] native_video_startup_warmup_hold"
                            + " startup_seconds=" + StartupElapsedSeconds.ToString("F3")
                            + " frame_index=" + frameInfo.FrameIndex
                            + " frame_time=" + frameInfo.TimeSec.ToString("F3")
                            + " stable_count=" + _nativeVideoStartupWarmupStableFrameCount
                            + " stable_target=" + FileNativeVideoStartupWarmupStableFrames
                            + " suppressed_total=" + _nativeVideoStartupWarmupSuppressedFrameCount
                            + " frame_index_delta=" + frameIndexDelta
                            + " frame_time_delta_ms=" + (frameTimeDeltaSec * 1000.0).ToString("F1")
                            + " realtime_delta_ms=" + (realtimeDeltaSec * 1000.0).ToString("F1"));
                    }
                    return;
                }

                var presentationPath = NativeVideoPresentationPathKind.None;
                var handled = false;
                if (PreferNativeVideoRenderEventPass)
                {
                    handled = TryUseNativeRenderTarget(frameInfo);
                    if (handled)
                    {
                        presentationPath = _nativeVideoPresentationPath;
                    }
                }

                if (PreferNativeVideoUnityDirectShader)
                {
                    if (!handled)
                    {
                        handled = TryBindNativeVideoPlaneTexturesDirect(frameInfo);
                        if (handled)
                        {
                            presentationPath = _nativeVideoPresentationPath;
                        }
                    }
                }

                if (!handled && PreferNativeVideoUnityCompute)
                {
                    handled = TryRenderNativeVideoPlaneTextures(frameInfo);
                    if (handled)
                    {
                        presentationPath = _nativeVideoPresentationPath;
                    }
                }

                if (!handled && CanDirectlyBindNativeFrame(frameInfo))
                {
                    handled = TryBindNativeTexture(frameInfo);
                    if (handled)
                    {
                        presentationPath = _nativeVideoPresentationPath;
                    }
                }

                if (handled)
                {
                    _nativeVideoFramePresentedCount += 1;
                    _nativeVideoFramePresentedLifetimeCount += 1;
                    _nativeVideoStartupWarmupCompleted = true;
                    switch (presentationPath)
                    {
                        case NativeVideoPresentationPathKind.RenderEventPass:
                            _nativeVideoFrameRenderEventPassCount += 1;
                            break;
                        case NativeVideoPresentationPathKind.DirectBind:
                            _nativeVideoFrameDirectBindPresentCount += 1;
                            break;
                        case NativeVideoPresentationPathKind.DirectShader:
                            _nativeVideoFrameDirectShaderPresentCount += 1;
                            break;
                        case NativeVideoPresentationPathKind.Compute:
                            _nativeVideoFrameComputePresentCount += 1;
                            break;
                    }
                }
                else
                {
                    _nativeVideoFramePresentationFailureCount += 1;
                }

                if (!handled && !_nativeVideoBindingWarningIssued)
                {
                    _nativeVideoBindingWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] native_video_frame acquired without supported presentation path"
                        + " surface=" + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " unity_direct_shader_requested="
                        + PreferNativeVideoUnityDirectShader
                        + " unity_compute_requested=" + PreferNativeVideoUnityCompute
                        + " source_plane_textures_supported="
                        + _nativeVideoInteropCaps.SourcePlaneTexturesSupported
                        + " flags=0x" + frameInfo.Flags.ToString("X"));
                }

                if (hasLastFrame
                    && (TraceNativeVideoCadence
                        || frameIndexDelta != 1
                        || realtimeDeltaSec >= 0.050f
                        || frameTimeDeltaSec <= 0.0))
                {
                    Debug.Log(
                        "[MediaPlayer] native_video_frame_acquired"
                        + " startup_seconds=" + StartupElapsedSeconds.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " frame_time=" + frameInfo.TimeSec.ToString("F3")
                        + " frame_index_delta=" + frameIndexDelta
                        + " frame_time_delta_ms=" + (frameTimeDeltaSec * 1000.0).ToString("F1")
                        + " realtime_delta_ms=" + (realtimeDeltaSec * 1000.0).ToString("F1")
                        + " acquire_attempts=" + _nativeVideoFrameAcquireAttemptCount
                        + " acquire_misses=" + _nativeVideoFrameAcquireMissCount
                        + " duplicates=" + _nativeVideoFrameDuplicateAcquireCount
                        + " duplicate_streak_max=" + _nativeVideoFrameMaxConsecutiveDuplicateCount
                        + " skip_total=" + _nativeVideoFrameSkippedFrameCount
                        + " non_monotonic=" + _nativeVideoFrameNonMonotonicCount
                        + " presented_total=" + _nativeVideoFramePresentedCount
                        + " presentation_failures=" + _nativeVideoFramePresentationFailureCount
                        + " presentation_path=" + presentationPath);
                }
            }
            finally
            {
                if (ReleaseNativeVideoFrameInfo(frameInfo.FrameIndex))
                {
                    _nativeVideoFrameReleaseCount += 1;
                }
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

        private void OnApplicationPause(bool pauseStatus)
        {
            if (ValidatePlayerId(_id))
            {
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
        }

        private void EnsureAudioSource()
        {
            if (!EnableAudio || _audioSource != null)
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

        private void UpdateAudioBuffer()
        {
            if (!EnableAudio || !ValidatePlayerId(_id))
            {
                return;
            }

            EnsureAudioSource();

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

                copied -= copied % bytesPerInterleavedSample;
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
                WriteAudioSamples(_audioFloats, sampleCount);

                if (copied < bytesToCopy)
                {
                    break;
                }
            }

            ObserveNativeBufferedAudioBytes(latestNativeBufferedBytes);
            TryStartAudioSource();
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

            EnsureAudioSource();
            if (_audioSource == null)
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
            }

            ObserveNativeBufferedAudioBytes(0);

            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            if (_audioClip != null)
            {
                if (ReferenceEquals(_audioSource.clip, _audioClip))
                {
                    _audioSource.clip = null;
                }

                Destroy(_audioClip);
            }

            _audioClip = AudioClip.Create(
                Uri + "_Audio",
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

        private void WriteAudioSamples(float[] samples, int sampleCount)
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
                    TrimBufferedAudioSamplesIfNeeded();
                    return;
                }

                var freeSamples = _audioRing.Length - _audioBufferedSamples;
                if (sampleCount > freeSamples)
                {
                    var dropSamples = sampleCount - freeSamples;
                    _audioReadIndex = (_audioReadIndex + dropSamples) % _audioRing.Length;
                    _audioBufferedSamples -= dropSamples;
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
            var steadyStateSamples = (_audioSampleRate * _audioChannels * steadyStateMilliseconds)
                / 1000;
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
                if (_audioBufferedSamples <= 0 || _audioRing == null || _audioRing.Length == 0)
                {
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
            }
        }

        private void OnAudioSetPosition(int position)
        {
            _ = position;
        }

        private void ClearAudioBuffer()
        {
            lock (_audioLock)
            {
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                if (_audioRing != null && _audioRing.Length > 0)
                {
                    Array.Clear(_audioRing, 0, _audioRing.Length);
                }
            }

            ObserveNativeBufferedAudioBytes(0);
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

                if (_isRealtimeSource && _nativeVideoPathActive && !HasPresentedNativeVideoFrame)
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
            UpdateNativeAudioSinkDelay();
        }

        private int CalculateAudioStartThresholdSamples()
        {
            if (_audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return 0;
            }

            var thresholdMilliseconds = _isRealtimeSource
                ? RealtimeAudioStartThresholdMilliseconds
                : FileAudioStartThresholdMilliseconds;
            var thresholdSamples = (_audioSampleRate * _audioChannels * thresholdMilliseconds)
                / 1000;
            if (!_isRealtimeSource)
            {
                return Math.Max(thresholdSamples, _audioChannels);
            }

            var startupElapsedMilliseconds = StartupElapsedSeconds * 1000f;
            var hasStartupFrame = !_nativeVideoPathActive || HasPresentedNativeVideoFrame;
            if (!hasStartupFrame || startupElapsedMilliseconds < RealtimeAudioStartupGraceMilliseconds)
            {
                return Math.Max(thresholdSamples, _audioChannels);
            }

            var relaxedThresholdSamples = (_audioSampleRate * _audioChannels
                * RealtimeAudioStartupMinimumThresholdMilliseconds) / 1000;
            return Math.Max(Math.Min(thresholdSamples, relaxedThresholdSamples), _audioChannels);
        }

        private void ObserveNativeBufferedAudioBytes(int bufferedBytes)
        {
            _nativeBufferedAudioBytes = Math.Max(0, bufferedBytes);
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
            if (!EnableAudio)
            {
                return 0.0;
            }

            var delaySec = 0.0;
            var audioStarted = _audioSource != null && _audioSource.isPlaying;
            if (_audioSampleRate > 0 && _audioChannels > 0)
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

        private void ResetManagedAudioState()
        {
            _audioBytes = new byte[0];
            _audioFloats = new float[0];
            _audioSampleRate = 0;
            _audioChannels = 0;
            _audioBytesPerSample = 0;
            _nativeBufferedAudioBytes = 0;
            lock (_audioLock)
            {
                _audioRing = new float[0];
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
            }
        }

        private static bool ValidatePlayerId(int id)
        {
            return id >= 0;
        }

        private static bool CanDirectlyBindNativeFrame(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return frameInfo.SurfaceKind == NativeVideoSurfaceKind.D3D11Texture2D
                && frameInfo.PixelFormat == NativeVideoPixelFormatKind.Rgba32
                && frameInfo.NativeHandle != IntPtr.Zero;
#else
            return false;
#endif
        }

        private bool TryUseNativeRenderTarget(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _nativeVideoRenderEventPassAttemptCount += 1;
            if (_targetTexture == null)
            {
                return false;
            }

            var targetHandle = _targetTexture.GetNativeTexturePtr();
            var auxiliaryHandle = ResolveNativeVideoAuxiliaryHandle(_targetTexture);
            if (targetHandle == IntPtr.Zero
                || frameInfo.SurfaceKind != NativeVideoSurfaceKind.D3D11Texture2D
                || frameInfo.PixelFormat != NativeVideoPixelFormatKind.Rgba32
                || (frameInfo.NativeHandle != targetHandle
                    && frameInfo.AuxiliaryHandle != targetHandle
                    && frameInfo.AuxiliaryHandle != auxiliaryHandle))
            {
                return false;
            }

            ApplyPresentedTexture(_targetTexture);

            if (_nativeVideoPresentationPath != NativeVideoPresentationPathKind.RenderEventPass)
            {
                Debug.Log(
                    "[MediaPlayer] native_render_target_present startup_seconds="
                    + StartupElapsedSeconds.ToString("F3")
                    + " frame_index=" + frameInfo.FrameIndex
                    + " handle=0x" + frameInfo.NativeHandle.ToInt64().ToString("X")
                    + " flags=0x" + frameInfo.Flags.ToString("X"));
            }

            _nativeTextureBound = false;
            _nativePlaneTexturesBound = false;
            _nativeVideoDirectShaderPathActive = false;
            _nativeVideoComputePathActive = false;
            _nativeVideoPresentationPath = NativeVideoPresentationPathKind.RenderEventPass;
            _nativeVideoRenderEventPassSuccessCount += 1;
            return true;
#else
            return false;
#endif
        }

        private static IntPtr ResolveNativeVideoAuxiliaryHandle(Texture targetTexture)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (targetTexture is RenderTexture renderTexture)
            {
                try
                {
                    return renderTexture.colorBuffer.GetNativeRenderBufferPtr();
                }
                catch (Exception)
                {
                    return IntPtr.Zero;
                }
            }
#endif

            return IntPtr.Zero;
        }

        private static bool CanUseUnityComputePlaneTextures(NativeVideoPlaneTexturesInfo texturesInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return texturesInfo.SurfaceKind == NativeVideoSurfaceKind.D3D11Texture2D
                && (texturesInfo.SourcePixelFormat == NativeVideoPixelFormatKind.Nv12
                    || texturesInfo.SourcePixelFormat == NativeVideoPixelFormatKind.P010)
                && texturesInfo.YNativeHandle != IntPtr.Zero
                && texturesInfo.UVNativeHandle != IntPtr.Zero
                && HasSupportedUnityPlaneTextureFormatPair(texturesInfo)
                && texturesInfo.YWidth > 0
                && texturesInfo.YHeight > 0
                && texturesInfo.UVWidth > 0
                && texturesInfo.UVHeight > 0;
#else
            return false;
#endif
        }

        private static bool HasSupportedUnityPlaneTextureFormatPair(
            NativeVideoPlaneTexturesInfo texturesInfo)
        {
            switch (texturesInfo.SourcePixelFormat)
            {
                case NativeVideoPixelFormatKind.Nv12:
                    return texturesInfo.YTextureFormat == NativeVideoPlaneTextureFormatKind.R8Unorm
                        && texturesInfo.UVTextureFormat == NativeVideoPlaneTextureFormatKind.Rg8Unorm;
                case NativeVideoPixelFormatKind.P010:
                    return texturesInfo.YTextureFormat == NativeVideoPlaneTextureFormatKind.R16Unorm
                        && texturesInfo.UVTextureFormat == NativeVideoPlaneTextureFormatKind.Rg16Unorm;
                default:
                    return false;
            }
        }

        private static bool IsBt2020ColorMatrix(NativeVideoColorMatrixKind matrix)
        {
            return matrix == NativeVideoColorMatrixKind.Bt2020Ncl
                || matrix == NativeVideoColorMatrixKind.Bt2020Cl;
        }

        private static NativeVideoColorInfo CreateDefaultNativeVideoPlaneColorInfo(
            NativeVideoPlaneTexturesInfo texturesInfo)
        {
            if (texturesInfo.SourcePixelFormat == NativeVideoPixelFormatKind.P010)
            {
                return new NativeVideoColorInfo
                {
                    Range = NativeVideoColorRangeKind.Limited,
                    Matrix = NativeVideoColorMatrixKind.Bt2020Ncl,
                    Primaries = NativeVideoColorPrimariesKind.Bt2020,
                    Transfer = NativeVideoTransferCharacteristicKind.Pq,
                    BitDepth = 10,
                    DynamicRange = NativeVideoDynamicRangeKind.Hdr10,
                };
            }

            return new NativeVideoColorInfo
            {
                Range = NativeVideoColorRangeKind.Limited,
                Matrix = NativeVideoColorMatrixKind.Bt709,
                Primaries = NativeVideoColorPrimariesKind.Bt709,
                Transfer = NativeVideoTransferCharacteristicKind.Bt1886,
                BitDepth = 8,
                DynamicRange = NativeVideoDynamicRangeKind.Sdr,
            };
        }

        private NativeVideoColorInfo ResolveEffectiveNativeVideoPlaneColorInfo(
            NativeVideoPlaneTexturesInfo texturesInfo)
        {
            var colorInfo = CreateDefaultNativeVideoPlaneColorInfo(texturesInfo);
            NativeVideoColorInfo sourceColorInfo;
            if (TryGetNativeVideoSourceColorInfo(out sourceColorInfo))
            {
                if (sourceColorInfo.Range != NativeVideoColorRangeKind.Unknown)
                {
                    colorInfo.Range = sourceColorInfo.Range;
                }

                if (sourceColorInfo.Matrix != NativeVideoColorMatrixKind.Unknown)
                {
                    colorInfo.Matrix = sourceColorInfo.Matrix;
                }

                if (sourceColorInfo.Primaries != NativeVideoColorPrimariesKind.Unknown)
                {
                    colorInfo.Primaries = sourceColorInfo.Primaries;
                }

                if (sourceColorInfo.Transfer != NativeVideoTransferCharacteristicKind.Unknown)
                {
                    colorInfo.Transfer = sourceColorInfo.Transfer;
                }

                if (sourceColorInfo.BitDepth > 0)
                {
                    colorInfo.BitDepth = sourceColorInfo.BitDepth;
                }

                if (sourceColorInfo.DynamicRange != NativeVideoDynamicRangeKind.Unknown)
                {
                    colorInfo.DynamicRange = sourceColorInfo.DynamicRange;
                }
            }

            if (colorInfo.Range == NativeVideoColorRangeKind.Unknown)
            {
                colorInfo.Range = NativeVideoColorRangeKind.Limited;
            }

            if (colorInfo.Matrix == NativeVideoColorMatrixKind.Unknown)
            {
                colorInfo.Matrix = texturesInfo.SourcePixelFormat == NativeVideoPixelFormatKind.P010
                    ? NativeVideoColorMatrixKind.Bt2020Ncl
                    : NativeVideoColorMatrixKind.Bt709;
            }

            if (colorInfo.Primaries == NativeVideoColorPrimariesKind.Unknown)
            {
                colorInfo.Primaries = IsBt2020ColorMatrix(colorInfo.Matrix)
                    ? NativeVideoColorPrimariesKind.Bt2020
                    : NativeVideoColorPrimariesKind.Bt709;
            }

            if (colorInfo.BitDepth <= 0)
            {
                colorInfo.BitDepth = texturesInfo.SourcePixelFormat == NativeVideoPixelFormatKind.P010
                    ? 10
                    : 8;
            }

            if (colorInfo.DynamicRange == NativeVideoDynamicRangeKind.Unknown)
            {
                colorInfo.DynamicRange = colorInfo.Transfer == NativeVideoTransferCharacteristicKind.Pq
                    ? NativeVideoDynamicRangeKind.Hdr10
                    : NativeVideoDynamicRangeKind.Sdr;
            }

            if (colorInfo.Transfer == NativeVideoTransferCharacteristicKind.Unknown)
            {
                colorInfo.Transfer = colorInfo.DynamicRange == NativeVideoDynamicRangeKind.Hdr10
                    ? NativeVideoTransferCharacteristicKind.Pq
                    : NativeVideoTransferCharacteristicKind.Bt1886;
            }

            return colorInfo;
        }

        private void ApplyNativeVideoPlaneColorInfoToMaterial(
            NativeVideoPlaneTexturesInfo texturesInfo,
            NativeVideoColorInfo colorInfo)
        {
            if (TargetMaterial == null)
            {
                return;
            }

            TargetMaterial.SetFloat(
                VideoSourcePixelFormatPropertyId,
                (float)texturesInfo.SourcePixelFormat);
            TargetMaterial.SetFloat(
                VideoColorRangePropertyId,
                (float)colorInfo.Range);
            TargetMaterial.SetFloat(
                VideoColorMatrixPropertyId,
                (float)colorInfo.Matrix);
            TargetMaterial.SetFloat(
                VideoColorPrimariesPropertyId,
                (float)colorInfo.Primaries);
            TargetMaterial.SetFloat(
                VideoTransferPropertyId,
                (float)colorInfo.Transfer);
            TargetMaterial.SetFloat(
                VideoBitDepthPropertyId,
                colorInfo.BitDepth);
            TargetMaterial.SetFloat(
                VideoDynamicRangePropertyId,
                (float)colorInfo.DynamicRange);
        }

        private static void ApplyNativeVideoPlaneColorInfoToComputeShader(
            ComputeShader computeShader,
            NativeVideoPlaneTexturesInfo texturesInfo,
            NativeVideoColorInfo colorInfo)
        {
            computeShader.SetInt("_VideoSourcePixelFormat", (int)texturesInfo.SourcePixelFormat);
            computeShader.SetInt("_VideoColorRange", (int)colorInfo.Range);
            computeShader.SetInt("_VideoColorMatrix", (int)colorInfo.Matrix);
            computeShader.SetInt("_VideoColorPrimaries", (int)colorInfo.Primaries);
            computeShader.SetInt("_VideoTransfer", (int)colorInfo.Transfer);
            computeShader.SetInt("_VideoBitDepth", colorInfo.BitDepth);
            computeShader.SetInt("_VideoDynamicRange", (int)colorInfo.DynamicRange);
        }

        private static TextureFormat ResolveUnityExternalPlaneTextureFormat(
            NativeVideoPlaneTextureFormatKind textureFormat)
        {
            switch (textureFormat)
            {
                case NativeVideoPlaneTextureFormatKind.R8Unorm:
                    return TextureFormat.R8;
                case NativeVideoPlaneTextureFormatKind.Rg8Unorm:
                    return TextureFormat.RG16;
                case NativeVideoPlaneTextureFormatKind.R16Unorm:
                    return TextureFormat.R16;
                case NativeVideoPlaneTextureFormatKind.Rg16Unorm:
                    return TextureFormat.RG32;
                default:
                    throw new NotSupportedException(
                        "Unsupported native video plane texture format: " + textureFormat);
            }
        }
        private void ApplyPresentedTexture(Texture texture)
        {
            DisableNativeVideoPlaneTextureMode();
            if (TargetMaterial == null)
            {
                return;
            }

            if (!ReferenceEquals(TargetMaterial.mainTexture, texture))
            {
                TargetMaterial.mainTexture = texture;
            }
        }

        private bool TryBindNativeTexture(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _nativeVideoDirectBindAttemptCount += 1;
            try
            {
                var requiresRecreate = _boundNativeTexture == null
                    || _boundNativeTexture.width != frameInfo.Width
                    || _boundNativeTexture.height != frameInfo.Height;

                if (requiresRecreate)
                {
                    ReleaseBoundNativeTexture();
                    _boundNativeTexture = Texture2D.CreateExternalTexture(
                        frameInfo.Width,
                        frameInfo.Height,
                        TextureFormat.ARGB32,
                        false,
                        false,
                        frameInfo.NativeHandle);
                    _boundNativeTexture.filterMode = FilterMode.Bilinear;
                    _boundNativeTexture.name = Uri + "#NativeVideo";
                    _lastBoundNativeHandle = frameInfo.NativeHandle;
                }
                else if (_lastBoundNativeHandle != frameInfo.NativeHandle)
                {
                    _boundNativeTexture.UpdateExternalTexture(frameInfo.NativeHandle);
                    _lastBoundNativeHandle = frameInfo.NativeHandle;
                }

                ApplyPresentedTexture(_boundNativeTexture);

                if (!_nativeTextureBound)
                {
                    NativeVideoColorInfo colorInfo;
                    var hasColorInfo = TryGetNativeVideoColorInfo(out colorInfo);
                    Debug.Log(
                        "[MediaPlayer] native_texture_bound startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " surface=" + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " handle=0x" + frameInfo.NativeHandle.ToInt64().ToString("X")
                        + (hasColorInfo
                            ? " color_info=" + DescribeNativeVideoColorInfo(colorInfo)
                            : string.Empty));
                }

                _nativeTextureBound = true;
                _nativePlaneTexturesBound = false;
                _nativeVideoDirectShaderPathActive = false;
                _nativeVideoComputePathActive = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.DirectBind;
                _nativeTextureBindCount += 1;
                _nativeVideoDirectBindSuccessCount += 1;
                return true;
            }
            catch (Exception exception)
            {
                if (!_nativeVideoBindingWarningIssued)
                {
                    _nativeVideoBindingWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] direct_texture_binding failed surface="
                        + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " handle=0x" + frameInfo.NativeHandle.ToInt64().ToString("X")
                        + " error=" + exception.Message);
                }

                _nativeTextureBound = false;
                _nativePlaneTexturesBound = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
                ApplyPresentedTexture(_targetTexture);
                return false;
            }
#endif
            return false;
        }

        private bool TryBindNativeVideoPlaneTexturesDirect(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _nativeVideoDirectShaderAttemptCount += 1;
            if (!_nativeVideoInteropCaps.SourcePlaneTexturesSupported)
            {
                _nativeVideoDirectShaderSourcePlaneTexturesUnsupportedCount += 1;
                return false;
            }

            var directShader = ResolveNativeVideoNv12DirectShader();
            if (directShader == null)
            {
                _nativeVideoDirectShaderShaderUnavailableCount += 1;
                if (!_nativeVideoDirectShaderWarningIssued)
                {
                    _nativeVideoDirectShaderWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_direct_shader requested but NV12Direct shader is unavailable");
                }

                return false;
            }

            NativeVideoPlaneTexturesInfo texturesInfo;
            if (!TryAcquireNativeVideoSourcePlaneTexturesInfo(out texturesInfo))
            {
                _nativeVideoDirectShaderAcquireSourcePlaneTexturesFailureCount += 1;
                return false;
            }

            if (!CanUseUnityComputePlaneTextures(texturesInfo))
            {
                _nativeVideoDirectShaderPlaneTexturesUsabilityFailureCount += 1;
                if (!_nativeVideoDirectShaderWarningIssued)
                {
                    _nativeVideoDirectShaderWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_direct_shader requested but source plane textures are not usable"
                        + " surface=" + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_texture_format=" + texturesInfo.YTextureFormat
                        + " uv_texture_format=" + texturesInfo.UVTextureFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X"));
                }

                return false;
            }

            var effectiveColorInfo = ResolveEffectiveNativeVideoPlaneColorInfo(texturesInfo);

            try
            {
                if (!EnsureNativeVideoDirectShaderMaterial(directShader))
                {
                    return false;
                }

                EnsureNativePlaneTextureBindings(texturesInfo);
                BindNativeVideoPlaneTexturesToMaterial(texturesInfo, effectiveColorInfo);

                if (!_nativeVideoDirectShaderPathActive)
                {
                    NativeVideoColorInfo colorInfo;
                    var hasColorInfo = TryGetNativeVideoSourceColorInfo(out colorInfo);
                    Debug.Log(
                        "[MediaPlayer] unity_direct_shader_bound startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_texture_format=" + texturesInfo.YTextureFormat
                        + " uv_texture_format=" + texturesInfo.UVTextureFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X")
                        + " source_flags=0x" + texturesInfo.Flags.ToString("X")
                        + " effective_color_info=" + DescribeNativeVideoColorInfo(effectiveColorInfo)
                        + (hasColorInfo
                            ? " source_color_info=" + DescribeNativeVideoColorInfo(colorInfo)
                            : string.Empty));
                }

                _nativeVideoDirectShaderPathActive = true;
                _nativePlaneTexturesBound = true;
                _nativePlaneTextureBindCount += 1;
                _nativeVideoDirectShaderBindCount += 1;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.DirectShader;
                _nativeTextureBound = false;
                _nativeVideoComputePathActive = false;
                _nativeVideoDirectShaderSuccessCount += 1;
                return true;
            }
            catch (Exception exception)
            {
                _nativeVideoDirectShaderExceptionCount += 1;
                if (!_nativeVideoDirectShaderWarningIssued)
                {
                    _nativeVideoDirectShaderWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_direct_shader failed source_surface="
                        + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_texture_format=" + texturesInfo.YTextureFormat
                        + " uv_texture_format=" + texturesInfo.UVTextureFormat
                        + " error=" + exception.Message);
                }

                _nativeVideoDirectShaderPathActive = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
                _nativePlaneTexturesBound = false;
                DisableNativeVideoPlaneTextureMode();
                ApplyPresentedTexture(_targetTexture);
                return false;
            }
#else
            return false;
#endif
        }

        private bool TryRenderNativeVideoPlaneTextures(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _nativeVideoComputeAttemptCount += 1;
            if (!_nativeVideoInteropCaps.SourcePlaneTexturesSupported)
            {
                _nativeVideoComputeSourcePlaneTexturesUnsupportedCount += 1;
                return false;
            }

            var computeShader = ResolveNativeVideoNv12ComputeShader();
            if (computeShader == null)
            {
                _nativeVideoComputeShaderUnavailableCount += 1;
                if (!_nativeVideoComputeWarningIssued)
                {
                    _nativeVideoComputeWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_compute requested but NV12ToRGBA compute shader is unavailable");
                }

                return false;
            }

            NativeVideoPlaneTexturesInfo texturesInfo;
            if (!TryAcquireNativeVideoSourcePlaneTexturesInfo(out texturesInfo))
            {
                _nativeVideoComputeAcquireSourcePlaneTexturesFailureCount += 1;
                return false;
            }

            if (!CanUseUnityComputePlaneTextures(texturesInfo))
            {
                _nativeVideoComputePlaneTexturesUsabilityFailureCount += 1;
                if (!_nativeVideoComputeWarningIssued)
                {
                    _nativeVideoComputeWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_compute requested but source plane textures are not usable"
                        + " surface=" + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_texture_format=" + texturesInfo.YTextureFormat
                        + " uv_texture_format=" + texturesInfo.UVTextureFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X"));
                }

                return false;
            }

            var effectiveColorInfo = ResolveEffectiveNativeVideoPlaneColorInfo(texturesInfo);

            try
            {
                EnsureNativeVideoComputeResources(
                    computeShader,
                    texturesInfo.YWidth,
                    texturesInfo.YHeight);
                EnsureNativePlaneTextureBindings(texturesInfo);
                ApplyNativeVideoPlaneColorInfoToComputeShader(
                    computeShader,
                    texturesInfo,
                    effectiveColorInfo);
                computeShader.SetTexture(_nativeVideoComputeKernel, "YPlane", _nativePlaneTextureY);
                computeShader.SetTexture(_nativeVideoComputeKernel, "UVPlane", _nativePlaneTextureUV);
                var threadGroupsX = Mathf.CeilToInt(texturesInfo.YWidth / 16.0f);
                var threadGroupsY = Mathf.CeilToInt(texturesInfo.YHeight / 16.0f);
                computeShader.Dispatch(_nativeVideoComputeKernel, threadGroupsX, threadGroupsY, 1);
                ApplyPresentedTexture(_nativeVideoComputeOutput);

                if (!_nativeVideoComputePathActive)
                {
                    NativeVideoColorInfo colorInfo;
                    var hasColorInfo = TryGetNativeVideoSourceColorInfo(out colorInfo);
                    Debug.Log(
                        "[MediaPlayer] unity_compute_bound startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_texture_format=" + texturesInfo.YTextureFormat
                        + " uv_texture_format=" + texturesInfo.UVTextureFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X")
                        + " effective_color_info=" + DescribeNativeVideoColorInfo(effectiveColorInfo)
                        + (hasColorInfo
                            ? " source_color_info=" + DescribeNativeVideoColorInfo(colorInfo)
                            : string.Empty));
                }

                _nativeVideoComputePathActive = true;
                _nativeVideoDirectShaderPathActive = false;
                _nativePlaneTexturesBound = true;
                _nativePlaneTextureBindCount += 1;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.Compute;
                _nativeTextureBound = false;
                _nativeVideoComputeSuccessCount += 1;
                return true;
            }
            catch (Exception exception)
            {
                _nativeVideoComputeExceptionCount += 1;
                if (!_nativeVideoComputeWarningIssued)
                {
                    _nativeVideoComputeWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_compute failed source_surface="
                        + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_texture_format=" + texturesInfo.YTextureFormat
                        + " uv_texture_format=" + texturesInfo.UVTextureFormat
                        + " error=" + exception.Message);
                }

                _nativeVideoComputePathActive = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
                _nativePlaneTexturesBound = false;
                ApplyPresentedTexture(_targetTexture);
                return false;
            }
#else
            return false;
#endif
        }

        private ComputeShader ResolveNativeVideoNv12ComputeShader()
        {
            if (NativeVideoNv12ComputeShader != null)
            {
                return NativeVideoNv12ComputeShader;
            }

            NativeVideoNv12ComputeShader = Resources.Load<ComputeShader>("NV12ToRGBA");
            return NativeVideoNv12ComputeShader;
        }

        private Shader ResolveNativeVideoNv12DirectShader()
        {
            if (NativeVideoNv12DirectShader != null)
            {
                return NativeVideoNv12DirectShader;
            }

            NativeVideoNv12DirectShader = Resources.Load<Shader>("NV12Direct");
            return NativeVideoNv12DirectShader;
        }

        private bool EnsureNativeVideoDirectShaderMaterial(Shader shader)
        {
            if (TargetMaterial == null || shader == null)
            {
                return false;
            }

            if (!_capturedTargetMaterialShader)
            {
                _originalTargetMaterialShader = TargetMaterial.shader;
                _capturedTargetMaterialShader = true;
            }

            if (!ReferenceEquals(TargetMaterial.shader, shader))
            {
                TargetMaterial.shader = shader;
            }

            return true;
        }

        private void BindNativeVideoPlaneTexturesToMaterial(
            NativeVideoPlaneTexturesInfo texturesInfo,
            NativeVideoColorInfo colorInfo)
        {
            if (TargetMaterial == null)
            {
                return;
            }

            TargetMaterial.SetFloat(UseNativeVideoPlaneTexturesPropertyId, 1.0f);
            TargetMaterial.SetFloat(FlipVerticalPropertyId, 1.0f);
            TargetMaterial.SetTexture(YPlanePropertyId, _nativePlaneTextureY);
            TargetMaterial.SetTexture(UVPlanePropertyId, _nativePlaneTextureUV);
            ApplyNativeVideoPlaneColorInfoToMaterial(texturesInfo, colorInfo);
        }

        private void DisableNativeVideoPlaneTextureMode()
        {
            if (TargetMaterial == null)
            {
                return;
            }

            TargetMaterial.SetFloat(UseNativeVideoPlaneTexturesPropertyId, 0.0f);
            TargetMaterial.SetTexture(YPlanePropertyId, null);
            TargetMaterial.SetTexture(UVPlanePropertyId, null);
        }

        private void RestoreTargetMaterialShader()
        {
            if (TargetMaterial != null
                && _capturedTargetMaterialShader
                && _originalTargetMaterialShader != null
                && !ReferenceEquals(TargetMaterial.shader, _originalTargetMaterialShader))
            {
                TargetMaterial.shader = _originalTargetMaterialShader;
            }

            _capturedTargetMaterialShader = false;
            _originalTargetMaterialShader = null;
        }

        private void EnsureNativeVideoComputeResources(
            ComputeShader computeShader,
            int width,
            int height)
        {
            if (_nativeVideoComputeKernel < 0)
            {
                _nativeVideoComputeKernel = computeShader.FindKernel("CSMain");
            }

            if (_nativeVideoComputeOutput == null
                || _nativeVideoComputeOutput.width != width
                || _nativeVideoComputeOutput.height != height)
            {
                ReleaseNativeVideoComputeOutput();
                _nativeVideoComputeOutput = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear,
                    name = Uri + "#NativeVideoCompute"
                };
                _nativeVideoComputeOutput.Create();
            }

            computeShader.SetInt("Width", width);
            computeShader.SetInt("Height", height);
            computeShader.SetTexture(_nativeVideoComputeKernel, "Result", _nativeVideoComputeOutput);
        }

        private void EnsureNativePlaneTextureBindings(NativeVideoPlaneTexturesInfo texturesInfo)
        {
            var yTextureFormat = ResolveUnityExternalPlaneTextureFormat(texturesInfo.YTextureFormat);
            var uvTextureFormat = ResolveUnityExternalPlaneTextureFormat(texturesInfo.UVTextureFormat);

            var requiresYRecreate = _nativePlaneTextureY == null
                || _nativePlaneTextureY.width != texturesInfo.YWidth
                || _nativePlaneTextureY.height != texturesInfo.YHeight
                || _lastBoundNativePlaneYFormat != texturesInfo.YTextureFormat;
            if (requiresYRecreate)
            {
                ReleaseNativePlaneTexture(ref _nativePlaneTextureY);
                _nativePlaneTextureY = Texture2D.CreateExternalTexture(
                    texturesInfo.YWidth,
                    texturesInfo.YHeight,
                    yTextureFormat,
                    false,
                    false,
                    texturesInfo.YNativeHandle);
                _nativePlaneTextureY.filterMode = FilterMode.Bilinear;
                _nativePlaneTextureY.name = Uri + "#NativeVideoY";
            }
            else if (_lastBoundNativePlaneYHandle != texturesInfo.YNativeHandle)
            {
                _nativePlaneTextureY.UpdateExternalTexture(texturesInfo.YNativeHandle);
            }

            _lastBoundNativePlaneYHandle = texturesInfo.YNativeHandle;
            _lastBoundNativePlaneYFormat = texturesInfo.YTextureFormat;

            var requiresUVRecreate = _nativePlaneTextureUV == null
                || _nativePlaneTextureUV.width != texturesInfo.UVWidth
                || _nativePlaneTextureUV.height != texturesInfo.UVHeight
                || _lastBoundNativePlaneUVFormat != texturesInfo.UVTextureFormat;
            if (requiresUVRecreate)
            {
                ReleaseNativePlaneTexture(ref _nativePlaneTextureUV);
                _nativePlaneTextureUV = Texture2D.CreateExternalTexture(
                    texturesInfo.UVWidth,
                    texturesInfo.UVHeight,
                    uvTextureFormat,
                    false,
                    false,
                    texturesInfo.UVNativeHandle);
                _nativePlaneTextureUV.filterMode = FilterMode.Bilinear;
                _nativePlaneTextureUV.name = Uri + "#NativeVideoUV";
            }
            else if (_lastBoundNativePlaneUVHandle != texturesInfo.UVNativeHandle)
            {
                _nativePlaneTextureUV.UpdateExternalTexture(texturesInfo.UVNativeHandle);
            }

            _lastBoundNativePlaneUVHandle = texturesInfo.UVNativeHandle;
            _lastBoundNativePlaneUVFormat = texturesInfo.UVTextureFormat;
        }

        private void ReleaseBoundNativeTexture()
        {
            if (_boundNativeTexture == null)
            {
                return;
            }

            if (TargetMaterial != null && ReferenceEquals(TargetMaterial.mainTexture, _boundNativeTexture))
            {
                TargetMaterial.mainTexture = null;
            }

            Destroy(_boundNativeTexture);
            _boundNativeTexture = null;
            _lastBoundNativeHandle = IntPtr.Zero;
        }

        private void ReleaseBoundNativePlaneTextures()
        {
            ReleaseNativePlaneTexture(ref _nativePlaneTextureY);
            ReleaseNativePlaneTexture(ref _nativePlaneTextureUV);
            _lastBoundNativePlaneYHandle = IntPtr.Zero;
            _lastBoundNativePlaneUVHandle = IntPtr.Zero;
            _lastBoundNativePlaneYFormat = NativeVideoPlaneTextureFormatKind.Unknown;
            _lastBoundNativePlaneUVFormat = NativeVideoPlaneTextureFormatKind.Unknown;
        }
        private void ReleaseNativePlaneTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            Destroy(texture);
            texture = null;
        }

        private void ReleaseNativeVideoComputeOutput()
        {
            if (_nativeVideoComputeOutput == null)
            {
                return;
            }

            _nativeVideoComputeOutput.Release();
            Destroy(_nativeVideoComputeOutput);
            _nativeVideoComputeOutput = null;
        }

        private static bool HasNativeVideoFrameFlag(uint flags, uint expectedFlag)
        {
            return (flags & expectedFlag) == expectedFlag;
        }

        private void ResetNativeVideoTelemetry()
        {
            _playRequestedRealtimeAt = -1f;
            _firstNativeVideoFrameRealtimeAt = -1f;
            _lastNativeVideoFrameInfo = default(NativeVideoFrameInfo);
            _hasLastNativeVideoFrameInfo = false;
            _lastAcquiredNativeFrameIndex = -1;
            _lastNativeVideoFrameAcquireRealtimeAt = -1f;
            _nativeVideoFrameAcquireCount = 0;
            _nativeVideoFrameReleaseCount = 0;
            _nativeVideoFramePresentedLifetimeCount = 0;
            _nativeVideoStartupWarmupStableFrameCount = 0;
            _nativeVideoStartupWarmupSuppressedFrameCount = 0;
            _nativeVideoStartupWarmupCompleted = false;
            ResetNativeVideoFrameCadenceStats();
            ResetNativeVideoUpdateTimingStats();
            _nativeVideoBindingWarningIssued = false;
            _nativeVideoDirectShaderWarningIssued = false;
            _nativeVideoComputeWarningIssued = false;
            _nativeTextureBound = false;
            _nativePlaneTexturesBound = false;
            _nativeVideoDirectShaderPathActive = false;
            _nativeVideoComputePathActive = false;
            _nativeVideoSourceSurfaceZeroCopyActive = false;
            _nativeVideoSourcePlaneTexturesZeroCopyActive = false;
            _nativeTextureBindCount = 0;
            _nativePlaneTextureBindCount = 0;
            _nativeVideoDirectShaderBindCount = 0;
            _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
            _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.NotRequested;
            _lastBoundNativeHandle = IntPtr.Zero;
            _lastBoundNativePlaneYHandle = IntPtr.Zero;
            _lastBoundNativePlaneUVHandle = IntPtr.Zero;
            _lastBoundNativePlaneYFormat = NativeVideoPlaneTextureFormatKind.Unknown;
            _lastBoundNativePlaneUVFormat = NativeVideoPlaneTextureFormatKind.Unknown;
            ResetNativeVideoPresentationTelemetryStats();
        }

        private void ResetNativeVideoFrameCadenceStats()
        {
            _nativeVideoFrameAcquireAttemptCount = 0;
            _nativeVideoFrameAcquireMissCount = 0;
            _nativeVideoFrameDuplicateAcquireCount = 0;
            _nativeVideoFrameConsecutiveDuplicateCount = 0;
            _nativeVideoFrameMaxConsecutiveDuplicateCount = 0;
            _nativeVideoFrameConsecutiveMissCount = 0;
            _nativeVideoFrameMaxConsecutiveMissCount = 0;
            _nativeVideoFramePresentedCount = 0;
            _nativeVideoFramePresentationFailureCount = 0;
            _nativeVideoFrameRenderEventPassCount = 0;
            _nativeVideoFrameDirectBindPresentCount = 0;
            _nativeVideoFrameDirectShaderPresentCount = 0;
            _nativeVideoFrameComputePresentCount = 0;
            _nativeVideoFrameSkippedFrameCount = 0;
            _nativeVideoFrameNonMonotonicCount = 0;
            _nativeVideoFrameCadenceSampleCount = 0;
            _nativeVideoFrameLastIndexDelta = 0;
            _nativeVideoFrameLastTimeDeltaSec = 0.0;
            _nativeVideoFrameLastRealtimeDeltaSec = 0.0;
            _nativeVideoFrameTimeDeltaSumSec = 0.0;
            _nativeVideoFrameTimeDeltaMinSec = double.PositiveInfinity;
            _nativeVideoFrameTimeDeltaMaxSec = 0.0;
            _nativeVideoFrameRealtimeDeltaSumSec = 0.0;
            _nativeVideoFrameRealtimeDeltaMinSec = double.PositiveInfinity;
            _nativeVideoFrameRealtimeDeltaMaxSec = 0.0;
        }

        private void ResetNativeVideoUpdateTimingStats()
        {
            _nativeVideoUpdateCount = 0;
            _lastNativeVideoUpdateRealtimeAt = -1f;
            _nativeVideoStartupUpdateLogCount = 0;
            _nativeVideoUpdatePlayerElapsedMsSum = 0.0;
            _nativeVideoUpdatePlayerElapsedMsMax = 0.0;
            _nativeVideoUpdateNativeVideoFrameElapsedMsSum = 0.0;
            _nativeVideoUpdateNativeVideoFrameElapsedMsMax = 0.0;
            _nativeVideoUpdateAudioBufferElapsedMsSum = 0.0;
            _nativeVideoUpdateAudioBufferElapsedMsMax = 0.0;
        }

        private void ResetNativeVideoPresentationTelemetryStats()
        {
            _nativeVideoRenderEventPassAttemptCount = 0;
            _nativeVideoRenderEventPassSuccessCount = 0;
            _nativeVideoDirectBindAttemptCount = 0;
            _nativeVideoDirectBindSuccessCount = 0;
            _nativeVideoDirectShaderAttemptCount = 0;
            _nativeVideoDirectShaderSuccessCount = 0;
            _nativeVideoDirectShaderSourcePlaneTexturesUnsupportedCount = 0;
            _nativeVideoDirectShaderShaderUnavailableCount = 0;
            _nativeVideoDirectShaderAcquireSourcePlaneTexturesFailureCount = 0;
            _nativeVideoDirectShaderPlaneTexturesUsabilityFailureCount = 0;
            _nativeVideoDirectShaderMaterialFailureCount = 0;
            _nativeVideoDirectShaderExceptionCount = 0;
            _nativeVideoComputeAttemptCount = 0;
            _nativeVideoComputeSuccessCount = 0;
            _nativeVideoComputeSourcePlaneTexturesUnsupportedCount = 0;
            _nativeVideoComputeShaderUnavailableCount = 0;
            _nativeVideoComputeAcquireSourcePlaneTexturesFailureCount = 0;
            _nativeVideoComputePlaneTexturesUsabilityFailureCount = 0;
            _nativeVideoComputeExceptionCount = 0;
        }

        private bool ShouldWarmupFileNativeVideoStartupPresentation()
        {
            return !_isRealtimeSource
                && _nativeVideoPathActive
                && _nativeVideoInteropCaps.ExternalTextureTarget
                && !_nativeVideoStartupWarmupCompleted
                && _nativeVideoFramePresentedLifetimeCount == 0;
        }

        private bool ShouldHoldFileNativeVideoStartupPresentation(
            bool hasLastFrame,
            long frameIndexDelta,
            double frameTimeDeltaSec,
            float realtimeDeltaSec)
        {
            if (!ShouldWarmupFileNativeVideoStartupPresentation())
            {
                return false;
            }

            if (!hasLastFrame)
            {
                _nativeVideoStartupWarmupStableFrameCount = 0;
                return true;
            }

            var stableCadence = frameIndexDelta == 1
                && frameTimeDeltaSec > 0.0
                && realtimeDeltaSec > 0.0f
                && realtimeDeltaSec <= 0.025f;
            if (stableCadence)
            {
                _nativeVideoStartupWarmupStableFrameCount += 1;
            }
            else
            {
                _nativeVideoStartupWarmupStableFrameCount = 0;
            }

            return _nativeVideoStartupWarmupStableFrameCount
                < FileNativeVideoStartupWarmupStableFrames;
        }

        private static double ElapsedMilliseconds(long startTicks)
        {
            return (DiagnosticsStopwatch.GetTimestamp() - startTicks) * 1000.0
                / DiagnosticsStopwatch.Frequency;
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

            ClearAudioBuffer();
            SetAudioSinkDelaySeconds(_id, 0.0);
            NativeInitializer.UnregisterPlayerRenderEvent(_id);
            var result = ReleasePlayer(_id);
            _id = InvalidPlayerId;
            _playRequested = false;
            _resumeAfterPause = false;
            _actualBackendKind = MediaBackendKind.Auto;
            _nativeVideoPathActive = false;
            _nativeVideoInteropCaps = default(MediaNativeInteropCommon.NativeVideoInteropCapsView);
            _isRealtimeSource = false;
            ResetNativeVideoTelemetry();

            if (result < 0)
            {
                throw new Exception($"Failed to release player with error: {result}");
            }
        }

        private void ReleaseNativePlayerSilently()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            try
            {
                if (_audioSource != null)
                {
                    _audioSource.Stop();
                }

                ClearAudioBuffer();
                SetAudioSinkDelaySeconds(_id, 0.0);
                NativeInitializer.UnregisterPlayerRenderEvent(_id);
                ReleasePlayer(_id);
            }
            catch
            {
            }

            _id = InvalidPlayerId;
            _playRequested = false;
            _resumeAfterPause = false;
            _actualBackendKind = MediaBackendKind.Auto;
            _nativeVideoPathActive = false;
            _nativeVideoInteropCaps = default(MediaNativeInteropCommon.NativeVideoInteropCapsView);
            _isRealtimeSource = false;
            ResetNativeVideoTelemetry();
        }

        private void ReleaseManagedResources()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
                if (ReferenceEquals(_audioSource.clip, _audioClip))
                {
                    _audioSource.clip = null;
                }
            }

            if (_audioClip != null)
            {
                Destroy(_audioClip);
                _audioClip = null;
            }

            ResetManagedAudioState();

            if (TargetMaterial != null
                && (ReferenceEquals(TargetMaterial.mainTexture, _targetTexture)
                    || ReferenceEquals(TargetMaterial.mainTexture, _boundNativeTexture)
                    || ReferenceEquals(TargetMaterial.mainTexture, _nativeVideoComputeOutput)))
            {
                TargetMaterial.mainTexture = null;
            }

            DisableNativeVideoPlaneTextureMode();
            ReleaseBoundNativeTexture();
            ReleaseBoundNativePlaneTextures();
            ReleaseNativeVideoComputeOutput();
            RestoreTargetMaterialShader();
            _nativeVideoComputeKernel = -1;

            if (_targetTexture != null)
            {
                Destroy(_targetTexture);
                _targetTexture = null;
            }
        }

        private Texture CreateNativeVideoTargetTexture()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (PreferNativeVideo)
            {
                var descriptor = new RenderTextureDescriptor(Width, Height)
                {
                    depthBufferBits = 0,
                    msaaSamples = 1,
                    volumeDepth = 1,
                    mipCount = 1,
                    graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    sRGB = false,
                    useMipMap = false,
                    autoGenerateMips = false,
                    enableRandomWrite = false,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2D
                };
                var target = new RenderTexture(descriptor)
                {
                    filterMode = FilterMode.Bilinear,
                    name = Uri + "#NativeVideoTarget"
                };
                target.Create();
                return target;
            }
#endif

            return new Texture2D(Width, Height, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                name = Uri
            };
        }

        private uint ResolveNativeVideoTargetExtraFlags()
        {
            var flags = MediaNativeInteropCommon.NativeVideoTargetFlagNone;
            if (!PreferNativeVideoRenderEventPass
                && (PreferNativeVideoUnityDirectShader || PreferNativeVideoUnityCompute))
            {
                flags |= MediaNativeInteropCommon.NativeVideoTargetFlagDisableDirectTargetPresent;
            }

            return flags;
        }

        private static string DescribeTextureGraphicsFormat(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.graphicsFormat.ToString();
            }

            return "n/a";
        }

        private static string DescribeTextureMsaa(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.antiAliasing.ToString();
            }

            return "n/a";
        }

        private static string DescribeTextureUseMipMap(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.useMipMap.ToString();
            }

            return "n/a";
        }

        private static string DescribeTextureRandomWrite(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.enableRandomWrite.ToString();
            }

            return "n/a";
        }

        private static string DescribeNativeVideoColorInfo(NativeVideoColorInfo colorInfo)
        {
            return "range=" + colorInfo.Range
                + " matrix=" + colorInfo.Matrix
                + " primaries=" + colorInfo.Primaries
                + " transfer=" + colorInfo.Transfer
                + " bit_depth=" + colorInfo.BitDepth
                + " dynamic_range=" + colorInfo.DynamicRange;
        }

        private MediaBackendKind ReadActualBackendKind()
        {
            if (!ValidatePlayerId(_id))
            {
                return MediaBackendKind.Auto;
            }

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

        private string ReadBackendRuntimeDiagnostic(string uri)
        {
            return MediaNativeInteropCommon.ReadBackendRuntimeDiagnostic(
                GetBackendRuntimeDiagnostic,
                PreferredBackend,
                uri,
                EnableAudio);
        }

        private void TryCreateNativeVideoPlayer(
            string uri,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget nativeTarget,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions openOptions)
        {
            Debug.Log(
                "[MediaPlayer] native_video_create_begin"
                + " uri=" + uri
                + " backend=" + PreferredBackend
                + " strict_backend=" + StrictBackend
                + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null")
                + " target_handle=0x" + nativeTarget.TargetHandle.ToString("X")
                + " size=" + nativeTarget.Width + "x" + nativeTarget.Height
                + " platform=" + nativeTarget.PlatformKind
                + " surface=" + nativeTarget.SurfaceKind
                + " requested_surface=" + PreferredNativeVideoSurface
                + " target_provider="
                + MediaNativeInteropCommon.DescribeNativeVideoTargetProvider(
                    (NativeVideoPlatformKind)nativeTarget.PlatformKind,
                    (NativeVideoSurfaceKind)nativeTarget.SurfaceKind)
                + " target_contract="
                + MediaNativeInteropCommon.DescribeNativeVideoTargetContract(
                    (NativeVideoPlatformKind)nativeTarget.PlatformKind,
                    (NativeVideoSurfaceKind)nativeTarget.SurfaceKind)
                + " pixel_format=" + nativeTarget.PixelFormat
                + " flags=0x" + nativeTarget.Flags.ToString("X"));
            if (nativeTarget.TargetHandle == 0
                || nativeTarget.PlatformKind == (int)NativeVideoPlatformKind.Unknown
                || nativeTarget.SurfaceKind == (int)NativeVideoSurfaceKind.Unknown)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.InvalidTarget;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=invalid-target");
                return;
            }

            MediaNativeInteropCommon.NativeVideoInteropCapsView capsView;
            if (!MediaNativeInteropCommon.TryReadNativeVideoInteropCaps(
                GetNativeVideoInteropCaps,
                PreferredBackend,
                uri,
                ref nativeTarget,
                out capsView))
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.CapsUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_caps_unavailable");
                return;
            }

            _nativeVideoInteropCaps = capsView;
            Debug.Log(
                "[MediaPlayer] native_video_caps"
                + " supported=" + capsView.Supported
                + " contract_target_supported=" + capsView.ContractTargetSupported
                + " hardware_decode_supported=" + capsView.HardwareDecodeSupported
                + " zero_copy_supported=" + capsView.ZeroCopySupported
                + " acquire_release_supported=" + capsView.AcquireReleaseSupported
                + " runtime_bridge_pending=" + capsView.RuntimeBridgePending
                + " platform=" + capsView.PlatformKind
                + " surface=" + capsView.SurfaceKind
                + " target_provider="
                + MediaNativeInteropCommon.DescribeNativeVideoTargetProvider(
                    capsView.PlatformKind,
                    capsView.SurfaceKind)
                + " target_contract="
                + MediaNativeInteropCommon.DescribeNativeVideoTargetContract(
                    capsView.PlatformKind,
                    capsView.SurfaceKind)
                + " external_texture_target=" + capsView.ExternalTextureTarget
                + " source_surface_zero_copy_supported=" + capsView.SourceSurfaceZeroCopySupported
                + " presented_direct_bindable_supported=" + capsView.PresentedFrameDirectBindable
                + " presented_strict_zero_copy_supported=" + capsView.PresentedFrameStrictZeroCopySupported
                + " source_plane_textures_supported=" + capsView.SourcePlaneTexturesSupported
                + " source_plane_views_supported=" + capsView.SourcePlaneViewsSupported
                + " flags=0x" + capsView.Flags.ToString("X"));
            if (!capsView.Supported)
            {
                if (MediaNativeInteropCommon.IsNativeVideoContractBringUpPlatform(capsView.PlatformKind)
                    && MediaNativeInteropCommon.IsNativeVideoSurfaceKindSupportedByPlatform(
                        capsView.PlatformKind,
                        capsView.SurfaceKind))
                {
                    _nativeVideoActivationDecision =
                        NativeVideoActivationDecisionKind.PlatformRuntimeUnavailable;
                    Debug.LogWarning(
                        "[MediaPlayer] native_video_create_skipped reason=platform-runtime-unavailable"
                        + " platform=" + capsView.PlatformKind
                        + " surface=" + capsView.SurfaceKind
                        + " requested_surface=" + PreferredNativeVideoSurface
                        + " target_provider="
                        + MediaNativeInteropCommon.DescribeNativeVideoTargetProvider(
                            capsView.PlatformKind,
                            capsView.SurfaceKind)
                        + " target_contract="
                        + MediaNativeInteropCommon.DescribeNativeVideoTargetContract(
                            capsView.PlatformKind,
                            capsView.SurfaceKind)
                        + " contract_target_supported=" + capsView.ContractTargetSupported
                        + " runtime_bridge_pending=" + capsView.RuntimeBridgePending
                        + " require_hardware_decode=" + RequireNativeVideoHardwareDecode
                        + " require_zero_copy=" + RequireNativeVideoZeroCopy);
                }
                else
                {
                    _nativeVideoActivationDecision =
                        NativeVideoActivationDecisionKind.UnsupportedTarget;
                    Debug.LogWarning(
                        "[MediaPlayer] native_video_create_skipped reason=unsupported-target"
                        + " platform=" + capsView.PlatformKind
                        + " surface=" + capsView.SurfaceKind
                        + " requested_surface=" + PreferredNativeVideoSurface
                        + " target_provider="
                        + MediaNativeInteropCommon.DescribeNativeVideoTargetProvider(
                            capsView.PlatformKind,
                            capsView.SurfaceKind)
                        + " target_contract="
                        + MediaNativeInteropCommon.DescribeNativeVideoTargetContract(
                            capsView.PlatformKind,
                            capsView.SurfaceKind));
                }
                return;
            }

            if (RequireNativeVideoHardwareDecode && !capsView.HardwareDecodeSupported)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.HardwareDecodeUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=hardware-decode-unavailable");
                return;
            }

            if (RequireNativeVideoZeroCopy && !capsView.ZeroCopySupported)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.StrictZeroCopyUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=strict-zero-copy-unavailable");
                return;
            }

            if (!MediaNativeInteropCommon.IsNativeVideoPresentationPathImplementedForPlatform(
                    capsView.PlatformKind,
                    capsView.SurfaceKind))
            {
                _nativeVideoActivationDecision =
                    NativeVideoActivationDecisionKind.PlatformRuntimeUnavailable;
                Debug.LogWarning(
                    "[MediaPlayer] native_video_create_skipped reason=presentation-path-unavailable"
                    + " platform=" + capsView.PlatformKind
                    + " surface=" + capsView.SurfaceKind
                    + " target_provider="
                    + MediaNativeInteropCommon.DescribeNativeVideoTargetProvider(
                        capsView.PlatformKind,
                        capsView.SurfaceKind)
                    + " presentation_availability="
                    + MediaNativeInteropCommon.DescribeNativeVideoPresentationAvailability(
                        capsView.PlatformKind,
                        capsView.SurfaceKind)
                    + " contract_target_supported=" + capsView.ContractTargetSupported
                    + " runtime_bridge_pending=" + capsView.RuntimeBridgePending);
                return;
            }

            if (!capsView.AcquireReleaseSupported)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.AcquireReleaseUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=acquire-release-unavailable");
                return;
            }

            try
            {
                _id = GetNativeVideoPlayerEx(uri, ref nativeTarget, ref openOptions);
            }
            catch (EntryPointNotFoundException)
            {
                try
                {
                    _id = GetNativeVideoPlayer(uri, ref nativeTarget);
                }
                catch (EntryPointNotFoundException)
                {
                    _id = InvalidPlayerId;
                }
            }

            if (ValidatePlayerId(_id))
            {
                _nativeVideoPathActive = true;
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.Active;
                Debug.Log(
                    "[MediaPlayer] native_video_create_result"
                    + " player_id=" + _id
                    + " native_video_active=" + _nativeVideoPathActive);
            }
            else
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.CreateFailed;
                Debug.LogWarning(
                    "[MediaPlayer] native_video_create_failed"
                    + " player_id=" + _id
                    + " target_handle=0x" + nativeTarget.TargetHandle.ToString("X")
                    + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null"));
            }
        }
    }
}
