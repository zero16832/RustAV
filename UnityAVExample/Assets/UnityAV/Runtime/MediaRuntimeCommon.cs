using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace UnityAV
{
    public enum MediaBackendKind
    {
        Auto = 0,
        Ffmpeg = 1,
        Gstreamer = 2,
    }

    public enum MediaSourceConnectionState
    {
        Unknown = -1,
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        Checking = 4,
    }

    public enum NativeVideoPlatformKind
    {
        Unknown = 0,
        Windows = 1,
        Ios = 2,
        Android = 3,
    }

    public enum NativeVideoSurfaceKind
    {
        Unknown = 0,
        D3D11Texture2D = 1,
        MetalTexture = 2,
        CVPixelBuffer = 3,
        AndroidSurfaceTexture = 4,
        AndroidHardwareBuffer = 5,
    }

    public enum NativeVideoTargetProviderKind
    {
        Auto = 0,
        UnityExternalTexture = 1,
        IosMetalTexture = 2,
        IosCVPixelBuffer = 3,
        AndroidSurfaceTexture = 4,
        AndroidHardwareBuffer = 5,
    }

    public enum NativeVideoPixelFormatKind
    {
        Unknown = -1,
        Yuv420p = 0,
        Rgba32 = 1,
        Nv12 = 2,
        P010 = 3,
    }

    public enum NativeVideoColorRangeKind
    {
        Unknown = -1,
        Limited = 0,
        Full = 1,
    }

    public enum NativeVideoColorMatrixKind
    {
        Unknown = -1,
        Bt601 = 0,
        Bt709 = 1,
        Bt2020Ncl = 2,
        Bt2020Cl = 3,
        Smpte240M = 4,
        Rgb = 5,
    }

    public enum NativeVideoColorPrimariesKind
    {
        Unknown = -1,
        Bt601 = 0,
        Bt709 = 1,
        Bt2020 = 2,
        DciP3 = 3,
    }

    public enum NativeVideoTransferCharacteristicKind
    {
        Unknown = -1,
        Bt1886 = 0,
        Srgb = 1,
        Linear = 2,
        Smpte240M = 3,
        Pq = 4,
        Hlg = 5,
    }

    public enum NativeVideoDynamicRangeKind
    {
        Unknown = 0,
        Sdr = 1,
        Hdr10 = 2,
        Hlg = 3,
        DolbyVision = 4,
    }

    public enum NativeVideoPlaneTextureFormatKind
    {
        Unknown = 0,
        R8Unorm = 1,
        Rg8Unorm = 2,
        R16Unorm = 3,
        Rg16Unorm = 4,
    }

    public enum NativeVideoPlaneResourceKindKind
    {
        Unknown = 0,
        D3D11Texture2D = 1,
        D3D11ShaderResourceView = 2,
    }

    internal static class MediaNativeInteropCommon
    {
        internal const uint RustAVPlayerOpenOptionsVersion = 1u;
        internal const uint RustAVPlayerHealthSnapshotV2Version = 2u;
        internal const uint RustAVVideoColorInfoVersion = 1u;
        internal const uint RustAVNativeVideoTargetVersion = 1u;
        internal const uint RustAVNativeVideoInteropCapsVersion = 1u;
        internal const uint RustAVNativeVideoFrameVersion = 1u;
        internal const uint RustAVNativeVideoPlaneTexturesVersion = 1u;
        internal const uint RustAVNativeVideoPlaneViewsVersion = 1u;
        internal const int BackendDiagnosticBufferLength = 512;
        internal const uint NativeVideoTargetFlagNone = 0u;
        internal const uint NativeVideoTargetFlagExternalTexture = 1u << 0;
        internal const uint NativeVideoTargetFlagUnityOwnedTexture = 1u << 1;
        internal const uint NativeVideoTargetFlagDisableDirectTargetPresent = 1u << 2;
        internal const uint NativeVideoCapFlagTargetBindingSupported = 1u << 0;
        internal const uint NativeVideoCapFlagFrameAcquireSupported = 1u << 1;
        internal const uint NativeVideoCapFlagFrameReleaseSupported = 1u << 2;
        internal const uint NativeVideoCapFlagFallbackCopyPath = 1u << 3;
        internal const uint NativeVideoCapFlagExternalTextureTarget = 1u << 4;
        internal const uint NativeVideoCapFlagSourceSurfaceZeroCopy = 1u << 5;
        internal const uint NativeVideoCapFlagPresentedFrameDirectBindable = 1u << 6;
        internal const uint NativeVideoCapFlagPresentedFrameStrictZeroCopy = 1u << 7;
        internal const uint NativeVideoCapFlagSourcePlaneTexturesSupported = 1u << 8;
        internal const uint NativeVideoCapFlagSourcePlaneViewsSupported = 1u << 9;
        internal const uint NativeVideoCapFlagContractTargetSupported = 1u << 10;
        internal const uint NativeVideoCapFlagRuntimeBridgePending = 1u << 11;
        internal const uint NativeVideoFrameFlagNone = 0u;
        internal const uint NativeVideoFrameFlagHasFrame = 1u << 0;
        internal const uint NativeVideoFrameFlagHardwareDecode = 1u << 1;
        internal const uint NativeVideoFrameFlagZeroCopy = 1u << 2;
        internal const uint NativeVideoFrameFlagCpuFallback = 1u << 3;

        internal enum NativeVideoPixelFormat
        {
            Unknown = -1,
            Yuv420p = 0,
            Rgba32 = 1,
            Nv12 = 2,
            P010 = 3,
        }

        internal enum NativeVideoColorRange
        {
            Unknown = -1,
            Limited = 0,
            Full = 1,
        }

        internal enum NativeVideoColorMatrix
        {
            Unknown = -1,
            Bt601 = 0,
            Bt709 = 1,
            Bt2020Ncl = 2,
            Bt2020Cl = 3,
            Smpte240M = 4,
            Rgb = 5,
        }

        internal enum NativeVideoColorPrimaries
        {
            Unknown = -1,
            Bt601 = 0,
            Bt709 = 1,
            Bt2020 = 2,
            DciP3 = 3,
        }

        internal enum NativeVideoTransferCharacteristic
        {
            Unknown = -1,
            Bt1886 = 0,
            Srgb = 1,
            Linear = 2,
            Smpte240M = 3,
            Pq = 4,
            Hlg = 5,
        }

        internal enum NativeVideoDynamicRange
        {
            Unknown = 0,
            Sdr = 1,
            Hdr10 = 2,
            Hlg = 3,
            DolbyVision = 4,
        }

        internal enum NativeVideoPlaneTextureFormat
        {
            Unknown = 0,
            R8Unorm = 1,
            Rg8Unorm = 2,
            R16Unorm = 3,
            Rg16Unorm = 4,
        }

        internal enum NativeVideoPlaneResourceKind
        {
            Unknown = 0,
            D3D11Texture2D = 1,
            D3D11ShaderResourceView = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlayerOpenOptions
        {
            public uint StructSize;
            public uint StructVersion;
            public int BackendKind;
            public int StrictBackend;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoTarget
        {
            public uint StructSize;
            public uint StructVersion;
            public int PlatformKind;
            public int SurfaceKind;
            public ulong TargetHandle;
            public ulong AuxiliaryHandle;
            public int Width;
            public int Height;
            public int PixelFormat;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVVideoColorInfo
        {
            public uint StructSize;
            public uint StructVersion;
            public int Range;
            public int Matrix;
            public int Primaries;
            public int Transfer;
            public int BitDepth;
            public int DynamicRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoInteropCaps
        {
            public uint StructSize;
            public uint StructVersion;
            public int BackendKind;
            public int PlatformKind;
            public int SurfaceKind;
            public int Supported;
            public int HardwareDecodeSupported;
            public int ZeroCopySupported;
            public int AcquireReleaseSupported;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoFrame
        {
            public uint StructSize;
            public uint StructVersion;
            public int SurfaceKind;
            public ulong NativeHandle;
            public ulong AuxiliaryHandle;
            public int Width;
            public int Height;
            public int PixelFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoPlaneTextures
        {
            public uint StructSize;
            public uint StructVersion;
            public int SurfaceKind;
            public int SourcePixelFormat;
            public ulong YNativeHandle;
            public ulong YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public int YTextureFormat;
            public ulong UVNativeHandle;
            public ulong UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public int UVTextureFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoPlaneViews
        {
            public uint StructSize;
            public uint StructVersion;
            public int SurfaceKind;
            public int SourcePixelFormat;
            public ulong YNativeHandle;
            public ulong YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public int YTextureFormat;
            public int YResourceKind;
            public ulong UVNativeHandle;
            public ulong UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public int UVTextureFormat;
            public int UVResourceKind;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlayerHealthSnapshotV2
        {
            public uint StructSize;
            public uint StructVersion;
            public int State;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public int SourceConnectionState;
            public int IsConnected;
            public int IsPlaying;
            public int IsRealtime;
            public int CanSeek;
            public int IsLooping;
            public int StreamCount;
            public int VideoDecoderCount;
            public int HasAudioDecoder;
            public double DurationSec;
            public double CurrentTimeSec;
            public double AudioTimeSec;
            public double AudioPresentedTimeSec;
            public double AudioSinkDelaySec;
            public double ExternalTimeSec;
            public double VideoSyncCompensationSec;
            public long ConnectAttemptCount;
            public long VideoDecoderRecreateCount;
            public long AudioDecoderRecreateCount;
            public long VideoFrameDropCount;
            public long AudioFrameDropCount;
            public long SourcePacketCount;
            public long SourceTimeoutCount;
            public long SourceReconnectCount;
            public int SourceIsCheckingConnection;
            public double SourceLastActivityAgeSec;
        }

        internal delegate int BackendRuntimeDiagnosticDelegate(
            int backendKind,
            string path,
            bool requireAudioExport,
            StringBuilder destination,
            int destinationLength);

        internal delegate int GetPlayerHealthSnapshotDelegate(
            int playerId,
            ref RustAVPlayerHealthSnapshotV2 snapshot);

        internal delegate int GetNativeVideoInteropCapsDelegate(
            int backendKind,
            string path,
            ref RustAVNativeVideoTarget target,
            ref RustAVNativeVideoInteropCaps caps);

        internal delegate int GetNativeVideoColorInfoDelegate(
            int playerId,
            ref RustAVVideoColorInfo info);

        internal delegate int AcquireNativeVideoFrameDelegate(
            int playerId,
            ref RustAVNativeVideoFrame frame);

        internal delegate int GetNativeVideoSourcePlaneTexturesDelegate(
            int playerId,
            ref RustAVNativeVideoPlaneTextures textures);

        internal delegate int GetNativeVideoSourcePlaneViewsDelegate(
            int playerId,
            ref RustAVNativeVideoPlaneViews views);

        internal delegate int ReleaseNativeVideoFrameDelegate(
            int playerId,
            long frameIndex);

        internal struct RuntimeHealthView
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
            public double AudioTimeSec;
            public double AudioPresentedTimeSec;
            public double AudioSinkDelaySec;
        }

        internal struct NativeVideoInteropCapsView
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
            public bool PresentedFrameDirectBindable;
            public bool PresentedFrameStrictZeroCopySupported;
            public bool SourcePlaneTexturesSupported;
            public bool SourcePlaneViewsSupported;
            public bool AcquireReleaseSupported;
            public bool RuntimeBridgePending;
            public uint Flags;
        }

        internal struct NativeVideoFrameView
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public IntPtr NativeHandle;
            public IntPtr AuxiliaryHandle;
            public int Width;
            public int Height;
            public NativeVideoPixelFormat PixelFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        internal struct NativeVideoPlaneTexturesView
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormat SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormat YTextureFormat;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormat UVTextureFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        internal struct NativeVideoPlaneViewsView
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormat SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormat YTextureFormat;
            public NativeVideoPlaneResourceKind YResourceKind;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormat UVTextureFormat;
            public NativeVideoPlaneResourceKind UVResourceKind;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        internal struct VideoColorInfoView
        {
            public NativeVideoColorRange Range;
            public NativeVideoColorMatrix Matrix;
            public NativeVideoColorPrimaries Primaries;
            public NativeVideoTransferCharacteristic Transfer;
            public int BitDepth;
            public NativeVideoDynamicRange DynamicRange;
        }

        internal static bool TryReadRuntimeHealth(
            GetPlayerHealthSnapshotDelegate getPlayerHealthSnapshot,
            int playerId,
            out RuntimeHealthView health)
        {
            health = default(RuntimeHealthView);

            try
            {
                var snapshot = CreateHealthSnapshot();
                var result = getPlayerHealthSnapshot(playerId, ref snapshot);
                if (result < 0)
                {
                    return false;
                }

                health = new RuntimeHealthView
                {
                    State = snapshot.State,
                    RuntimeState = snapshot.RuntimeState,
                    PlaybackIntent = snapshot.PlaybackIntent,
                    StopReason = snapshot.StopReason,
                    SourceConnectionState = NormalizeSourceConnectionState(snapshot.SourceConnectionState),
                    IsConnected = snapshot.IsConnected != 0,
                    IsPlaying = snapshot.IsPlaying != 0,
                    IsRealtime = snapshot.IsRealtime != 0,
                    CanSeek = snapshot.CanSeek != 0,
                    IsLooping = snapshot.IsLooping != 0,
                    SourcePacketCount = snapshot.SourcePacketCount,
                    SourceTimeoutCount = snapshot.SourceTimeoutCount,
                    SourceReconnectCount = snapshot.SourceReconnectCount,
                    DurationSec = snapshot.DurationSec,
                    SourceLastActivityAgeSec = snapshot.SourceLastActivityAgeSec,
                    CurrentTimeSec = snapshot.CurrentTimeSec,
                    ExternalTimeSec = snapshot.ExternalTimeSec,
                    AudioTimeSec = snapshot.AudioTimeSec,
                    AudioPresentedTimeSec = snapshot.AudioPresentedTimeSec,
                    AudioSinkDelaySec = snapshot.AudioSinkDelaySec,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static RustAVPlayerOpenOptions CreateOpenOptions(
            MediaBackendKind preferredBackend,
            bool strictBackend)
        {
            return new RustAVPlayerOpenOptions
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerOpenOptions)),
                StructVersion = RustAVPlayerOpenOptionsVersion,
                BackendKind = (int)preferredBackend,
                StrictBackend = strictBackend ? 1 : 0,
            };
        }

        internal static RustAVPlayerHealthSnapshotV2 CreateHealthSnapshot()
        {
            return new RustAVPlayerHealthSnapshotV2
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerHealthSnapshotV2)),
                StructVersion = RustAVPlayerHealthSnapshotV2Version,
            };
        }

        internal static RustAVNativeVideoTarget CreateNativeVideoTarget(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind,
            IntPtr targetHandle,
            IntPtr auxiliaryHandle,
            int width,
            int height,
            uint extraFlags = NativeVideoTargetFlagNone)
        {
            var flags = NativeVideoTargetFlagExternalTexture;
            if (auxiliaryHandle != IntPtr.Zero)
            {
                flags |= NativeVideoTargetFlagUnityOwnedTexture;
            }
            flags |= extraFlags;

            return new RustAVNativeVideoTarget
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoTarget)),
                StructVersion = RustAVNativeVideoTargetVersion,
                PlatformKind = (int)platformKind,
                SurfaceKind = (int)surfaceKind,
                TargetHandle = unchecked((ulong)targetHandle.ToInt64()),
                AuxiliaryHandle = unchecked((ulong)auxiliaryHandle.ToInt64()),
                Width = width,
                Height = height,
                PixelFormat = (int)NativeVideoPixelFormat.Rgba32,
                Flags = flags,
            };
        }

        internal static RustAVNativeVideoTarget CreateDefaultNativeVideoTarget(
            IntPtr targetHandle,
            IntPtr auxiliaryHandle,
            int width,
            int height,
            NativeVideoSurfaceKind preferredSurfaceKind = NativeVideoSurfaceKind.Unknown,
            uint extraFlags = NativeVideoTargetFlagNone)
        {
            var platformKind = DetectNativeVideoPlatformKind();
            return CreateNativeVideoTarget(
                platformKind,
                ResolvePreferredNativeVideoSurfaceKind(platformKind, preferredSurfaceKind),
                targetHandle,
                auxiliaryHandle,
                width,
                height,
                extraFlags);
        }

        internal static RustAVNativeVideoInteropCaps CreateNativeVideoInteropCaps()
        {
            return new RustAVNativeVideoInteropCaps
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoInteropCaps)),
                StructVersion = RustAVNativeVideoInteropCapsVersion,
            };
        }

        internal static RustAVVideoColorInfo CreateVideoColorInfo()
        {
            return new RustAVVideoColorInfo
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVVideoColorInfo)),
                StructVersion = RustAVVideoColorInfoVersion,
            };
        }

        internal static RustAVNativeVideoFrame CreateNativeVideoFrame()
        {
            return new RustAVNativeVideoFrame
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoFrame)),
                StructVersion = RustAVNativeVideoFrameVersion,
            };
        }

        internal static RustAVNativeVideoPlaneTextures CreateNativeVideoPlaneTextures()
        {
            return new RustAVNativeVideoPlaneTextures
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoPlaneTextures)),
                StructVersion = RustAVNativeVideoPlaneTexturesVersion,
            };
        }

        internal static RustAVNativeVideoPlaneViews CreateNativeVideoPlaneViews()
        {
            return new RustAVNativeVideoPlaneViews
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoPlaneViews)),
                StructVersion = RustAVNativeVideoPlaneViewsVersion,
            };
        }

        internal static MediaBackendKind NormalizeBackendKind(
            int rawValue,
            MediaBackendKind fallback)
        {
            switch (rawValue)
            {
                case 1:
                    return MediaBackendKind.Ffmpeg;
                case 2:
                    return MediaBackendKind.Gstreamer;
                case 0:
                    return MediaBackendKind.Auto;
                default:
                    return fallback;
            }
        }

        internal static MediaSourceConnectionState NormalizeSourceConnectionState(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return MediaSourceConnectionState.Disconnected;
                case 1:
                    return MediaSourceConnectionState.Connecting;
                case 2:
                    return MediaSourceConnectionState.Connected;
                case 3:
                    return MediaSourceConnectionState.Reconnecting;
                case 4:
                    return MediaSourceConnectionState.Checking;
                default:
                    return MediaSourceConnectionState.Unknown;
            }
        }

        internal static string ReadBackendRuntimeDiagnostic(
            BackendRuntimeDiagnosticDelegate getBackendRuntimeDiagnostic,
            MediaBackendKind preferredBackend,
            string uri,
            bool requireAudioExport)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return string.Empty;
            }

            try
            {
                var buffer = new StringBuilder(BackendDiagnosticBufferLength);
                var result = getBackendRuntimeDiagnostic(
                    (int)preferredBackend,
                    uri,
                    requireAudioExport,
                    buffer,
                    buffer.Capacity);
                if (result >= 0 && buffer.Length > 0)
                {
                    return buffer.ToString();
                }
            }
            catch (EntryPointNotFoundException)
            {
                return string.Empty;
            }
            catch (DllNotFoundException)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        internal static bool TryReadNativeVideoInteropCaps(
            GetNativeVideoInteropCapsDelegate getNativeVideoInteropCaps,
            MediaBackendKind preferredBackend,
            string uri,
            ref RustAVNativeVideoTarget target,
            out NativeVideoInteropCapsView caps)
        {
            caps = default(NativeVideoInteropCapsView);

            try
            {
                var nativeCaps = CreateNativeVideoInteropCaps();
                var result = getNativeVideoInteropCaps(
                    (int)preferredBackend,
                    uri,
                    ref target,
                    ref nativeCaps);
                if (result < 0)
                {
                    return false;
                }

                caps = new NativeVideoInteropCapsView
                {
                    BackendKind = NormalizeBackendKind(nativeCaps.BackendKind, preferredBackend),
                    PlatformKind = NormalizeNativeVideoPlatformKind(nativeCaps.PlatformKind),
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeCaps.SurfaceKind),
                    Supported = nativeCaps.Supported != 0,
                    ContractTargetSupported =
                        (nativeCaps.Flags & NativeVideoCapFlagContractTargetSupported) != 0,
                    HardwareDecodeSupported = nativeCaps.HardwareDecodeSupported != 0,
                    ZeroCopySupported = nativeCaps.ZeroCopySupported != 0,
                    SourceSurfaceZeroCopySupported =
                        (nativeCaps.Flags & NativeVideoCapFlagSourceSurfaceZeroCopy) != 0,
                    ExternalTextureTarget =
                        (nativeCaps.Flags & NativeVideoCapFlagExternalTextureTarget) != 0,
                    PresentedFrameDirectBindable =
                        (nativeCaps.Flags & NativeVideoCapFlagPresentedFrameDirectBindable) != 0,
                    PresentedFrameStrictZeroCopySupported =
                        (nativeCaps.Flags & NativeVideoCapFlagPresentedFrameStrictZeroCopy) != 0,
                    SourcePlaneTexturesSupported =
                        (nativeCaps.Flags & NativeVideoCapFlagSourcePlaneTexturesSupported) != 0,
                    SourcePlaneViewsSupported =
                        (nativeCaps.Flags & NativeVideoCapFlagSourcePlaneViewsSupported) != 0,
                    AcquireReleaseSupported = nativeCaps.AcquireReleaseSupported != 0,
                    RuntimeBridgePending =
                        (nativeCaps.Flags & NativeVideoCapFlagRuntimeBridgePending) != 0,
                    Flags = nativeCaps.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadVideoColorInfo(
            GetNativeVideoColorInfoDelegate getNativeVideoColorInfo,
            int playerId,
            out VideoColorInfoView info)
        {
            info = default(VideoColorInfoView);

            try
            {
                var nativeInfo = CreateVideoColorInfo();
                var result = getNativeVideoColorInfo(playerId, ref nativeInfo);
                if (result <= 0)
                {
                    return false;
                }

                info = NormalizeVideoColorInfo(nativeInfo);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryAcquireNativeVideoFrame(
            AcquireNativeVideoFrameDelegate acquireNativeVideoFrame,
            int playerId,
            out NativeVideoFrameView frame)
        {
            frame = default(NativeVideoFrameView);

            try
            {
                var nativeFrame = CreateNativeVideoFrame();
                var result = acquireNativeVideoFrame(playerId, ref nativeFrame);
                if (result <= 0)
                {
                    return false;
                }

                frame = new NativeVideoFrameView
                {
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeFrame.SurfaceKind),
                    NativeHandle = new IntPtr(unchecked((long)nativeFrame.NativeHandle)),
                    AuxiliaryHandle = new IntPtr(unchecked((long)nativeFrame.AuxiliaryHandle)),
                    Width = nativeFrame.Width,
                    Height = nativeFrame.Height,
                    PixelFormat = NormalizeNativeVideoPixelFormat(nativeFrame.PixelFormat),
                    TimeSec = nativeFrame.TimeSec,
                    FrameIndex = nativeFrame.FrameIndex,
                    Flags = nativeFrame.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReleaseNativeVideoFrame(
            ReleaseNativeVideoFrameDelegate releaseNativeVideoFrame,
            int playerId,
            long frameIndex)
        {
            if (frameIndex < 0)
            {
                return false;
            }

            try
            {
                return releaseNativeVideoFrame(playerId, frameIndex) >= 0;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadNativeVideoSourcePlaneTextures(
            GetNativeVideoSourcePlaneTexturesDelegate getNativeVideoSourcePlaneTextures,
            int playerId,
            out NativeVideoPlaneTexturesView textures)
        {
            textures = default(NativeVideoPlaneTexturesView);

            try
            {
                var nativeTextures = CreateNativeVideoPlaneTextures();
                var result = getNativeVideoSourcePlaneTextures(playerId, ref nativeTextures);
                if (result <= 0)
                {
                    return false;
                }

                textures = new NativeVideoPlaneTexturesView
                {
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeTextures.SurfaceKind),
                    SourcePixelFormat =
                        NormalizeNativeVideoPixelFormat(nativeTextures.SourcePixelFormat),
                    YNativeHandle = new IntPtr(unchecked((long)nativeTextures.YNativeHandle)),
                    YAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeTextures.YAuxiliaryHandle)),
                    YWidth = nativeTextures.YWidth,
                    YHeight = nativeTextures.YHeight,
                    YTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeTextures.YTextureFormat),
                    UVNativeHandle = new IntPtr(unchecked((long)nativeTextures.UVNativeHandle)),
                    UVAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeTextures.UVAuxiliaryHandle)),
                    UVWidth = nativeTextures.UVWidth,
                    UVHeight = nativeTextures.UVHeight,
                    UVTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeTextures.UVTextureFormat),
                    TimeSec = nativeTextures.TimeSec,
                    FrameIndex = nativeTextures.FrameIndex,
                    Flags = nativeTextures.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadNativeVideoSourcePlaneViews(
            GetNativeVideoSourcePlaneViewsDelegate getNativeVideoSourcePlaneViews,
            int playerId,
            out NativeVideoPlaneViewsView views)
        {
            views = default(NativeVideoPlaneViewsView);

            try
            {
                var nativeViews = CreateNativeVideoPlaneViews();
                var result = getNativeVideoSourcePlaneViews(playerId, ref nativeViews);
                if (result <= 0)
                {
                    return false;
                }

                views = new NativeVideoPlaneViewsView
                {
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeViews.SurfaceKind),
                    SourcePixelFormat =
                        NormalizeNativeVideoPixelFormat(nativeViews.SourcePixelFormat),
                    YNativeHandle = new IntPtr(unchecked((long)nativeViews.YNativeHandle)),
                    YAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeViews.YAuxiliaryHandle)),
                    YWidth = nativeViews.YWidth,
                    YHeight = nativeViews.YHeight,
                    YTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeViews.YTextureFormat),
                    YResourceKind =
                        NormalizeNativeVideoPlaneResourceKind(nativeViews.YResourceKind),
                    UVNativeHandle = new IntPtr(unchecked((long)nativeViews.UVNativeHandle)),
                    UVAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeViews.UVAuxiliaryHandle)),
                    UVWidth = nativeViews.UVWidth,
                    UVHeight = nativeViews.UVHeight,
                    UVTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeViews.UVTextureFormat),
                    UVResourceKind =
                        NormalizeNativeVideoPlaneResourceKind(nativeViews.UVResourceKind),
                    TimeSec = nativeViews.TimeSec,
                    FrameIndex = nativeViews.FrameIndex,
                    Flags = nativeViews.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static NativeVideoPlatformKind DetectNativeVideoPlatformKind()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return NativeVideoPlatformKind.Windows;
#elif UNITY_IOS
            return NativeVideoPlatformKind.Ios;
#elif UNITY_ANDROID
            return NativeVideoPlatformKind.Android;
#else
            return NativeVideoPlatformKind.Unknown;
#endif
        }

        internal static NativeVideoSurfaceKind DetectDefaultNativeVideoSurfaceKind()
        {
            return GetDefaultNativeVideoSurfaceKindForPlatform(DetectNativeVideoPlatformKind());
        }

        internal static NativeVideoSurfaceKind GetDefaultNativeVideoSurfaceKindForPlatform(
            NativeVideoPlatformKind platformKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return NativeVideoSurfaceKind.D3D11Texture2D;
                case NativeVideoPlatformKind.Ios:
                    return NativeVideoSurfaceKind.MetalTexture;
                case NativeVideoPlatformKind.Android:
                    return NativeVideoSurfaceKind.AndroidSurfaceTexture;
                default:
                    return NativeVideoSurfaceKind.Unknown;
            }
        }

        internal static NativeVideoSurfaceKind ResolvePreferredNativeVideoSurfaceKind(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind preferredSurfaceKind)
        {
            if (preferredSurfaceKind == NativeVideoSurfaceKind.Unknown)
            {
                return GetDefaultNativeVideoSurfaceKindForPlatform(platformKind);
            }

            if (IsNativeVideoSurfaceKindSupportedByPlatform(platformKind, preferredSurfaceKind))
            {
                return preferredSurfaceKind;
            }

            return GetDefaultNativeVideoSurfaceKindForPlatform(platformKind);
        }

        internal static NativeVideoTargetProviderKind ResolveNativeVideoTargetProviderKind(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return NativeVideoTargetProviderKind.UnityExternalTexture;
                case NativeVideoPlatformKind.Ios:
                    if (surfaceKind == NativeVideoSurfaceKind.CVPixelBuffer)
                    {
                        return NativeVideoTargetProviderKind.IosCVPixelBuffer;
                    }
                    if (surfaceKind == NativeVideoSurfaceKind.MetalTexture)
                    {
                        return NativeVideoTargetProviderKind.IosMetalTexture;
                    }
                    break;
                case NativeVideoPlatformKind.Android:
                    if (surfaceKind == NativeVideoSurfaceKind.AndroidHardwareBuffer)
                    {
                        return NativeVideoTargetProviderKind.AndroidHardwareBuffer;
                    }
                    if (surfaceKind == NativeVideoSurfaceKind.AndroidSurfaceTexture)
                    {
                        return NativeVideoTargetProviderKind.AndroidSurfaceTexture;
                    }
                    break;
            }

            return NativeVideoTargetProviderKind.Auto;
        }

        internal static string DescribeNativeVideoTargetProvider(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            return ResolveNativeVideoTargetProviderKind(platformKind, surfaceKind).ToString();
        }

        internal static bool IsNativeVideoExternalTextureTargetSurface(
            NativeVideoSurfaceKind surfaceKind)
        {
            return surfaceKind == NativeVideoSurfaceKind.D3D11Texture2D
                || surfaceKind == NativeVideoSurfaceKind.MetalTexture;
        }

        internal static bool IsNativeVideoSurfaceKindSupportedByPlatform(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return surfaceKind == NativeVideoSurfaceKind.D3D11Texture2D;
                case NativeVideoPlatformKind.Ios:
                    return surfaceKind == NativeVideoSurfaceKind.MetalTexture
                        || surfaceKind == NativeVideoSurfaceKind.CVPixelBuffer;
                case NativeVideoPlatformKind.Android:
                    return surfaceKind == NativeVideoSurfaceKind.AndroidSurfaceTexture
                        || surfaceKind == NativeVideoSurfaceKind.AndroidHardwareBuffer;
                default:
                    return false;
            }
        }

        internal static bool IsNativeVideoContractBringUpPlatform(NativeVideoPlatformKind platformKind)
        {
            return platformKind == NativeVideoPlatformKind.Ios
                || platformKind == NativeVideoPlatformKind.Android;
        }

        internal static bool IsNativeVideoPresentationPathImplementedForPlatform(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return surfaceKind == NativeVideoSurfaceKind.D3D11Texture2D;
                case NativeVideoPlatformKind.Ios:
                case NativeVideoPlatformKind.Android:
                    return false;
                default:
                    return false;
            }
        }

        internal static string DescribeNativeVideoTargetContract(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            return platformKind + "/" + surfaceKind;
        }

        internal static string DescribeNativeVideoSurfaceSelection(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind preferredSurfaceKind,
            NativeVideoSurfaceKind resolvedSurfaceKind)
        {
            return "platform=" + platformKind
                + " preferred=" + preferredSurfaceKind
                + " resolved=" + resolvedSurfaceKind
                + " provider=" + DescribeNativeVideoTargetProvider(platformKind, resolvedSurfaceKind);
        }

        internal static string DescribeNativeVideoPresentationAvailability(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            return "platform=" + platformKind
                + " surface=" + surfaceKind
                + " implemented="
                + IsNativeVideoPresentationPathImplementedForPlatform(platformKind, surfaceKind);
        }

        internal static NativeVideoPlatformKind NormalizeNativeVideoPlatformKind(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoPlatformKind.Windows;
                case 2:
                    return NativeVideoPlatformKind.Ios;
                case 3:
                    return NativeVideoPlatformKind.Android;
                default:
                    return NativeVideoPlatformKind.Unknown;
            }
        }

        internal static NativeVideoSurfaceKind NormalizeNativeVideoSurfaceKind(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoSurfaceKind.D3D11Texture2D;
                case 2:
                    return NativeVideoSurfaceKind.MetalTexture;
                case 3:
                    return NativeVideoSurfaceKind.CVPixelBuffer;
                case 4:
                    return NativeVideoSurfaceKind.AndroidSurfaceTexture;
                case 5:
                    return NativeVideoSurfaceKind.AndroidHardwareBuffer;
                default:
                    return NativeVideoSurfaceKind.Unknown;
            }
        }

        internal static NativeVideoPixelFormat NormalizeNativeVideoPixelFormat(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoPixelFormat.Yuv420p;
                case 1:
                    return NativeVideoPixelFormat.Rgba32;
                case 2:
                    return NativeVideoPixelFormat.Nv12;
                case 3:
                    return NativeVideoPixelFormat.P010;
                default:
                    return NativeVideoPixelFormat.Unknown;
            }
        }

        internal static NativeVideoColorRange NormalizeNativeVideoColorRange(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoColorRange.Limited;
                case 1:
                    return NativeVideoColorRange.Full;
                default:
                    return NativeVideoColorRange.Unknown;
            }
        }

        internal static NativeVideoColorMatrix NormalizeNativeVideoColorMatrix(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoColorMatrix.Bt601;
                case 1:
                    return NativeVideoColorMatrix.Bt709;
                case 2:
                    return NativeVideoColorMatrix.Bt2020Ncl;
                case 3:
                    return NativeVideoColorMatrix.Bt2020Cl;
                case 4:
                    return NativeVideoColorMatrix.Smpte240M;
                case 5:
                    return NativeVideoColorMatrix.Rgb;
                default:
                    return NativeVideoColorMatrix.Unknown;
            }
        }

        internal static NativeVideoColorPrimaries NormalizeNativeVideoColorPrimaries(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoColorPrimaries.Bt601;
                case 1:
                    return NativeVideoColorPrimaries.Bt709;
                case 2:
                    return NativeVideoColorPrimaries.Bt2020;
                case 3:
                    return NativeVideoColorPrimaries.DciP3;
                default:
                    return NativeVideoColorPrimaries.Unknown;
            }
        }

        internal static NativeVideoTransferCharacteristic NormalizeNativeVideoTransferCharacteristic(
            int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoTransferCharacteristic.Bt1886;
                case 1:
                    return NativeVideoTransferCharacteristic.Srgb;
                case 2:
                    return NativeVideoTransferCharacteristic.Linear;
                case 3:
                    return NativeVideoTransferCharacteristic.Smpte240M;
                case 4:
                    return NativeVideoTransferCharacteristic.Pq;
                case 5:
                    return NativeVideoTransferCharacteristic.Hlg;
                default:
                    return NativeVideoTransferCharacteristic.Unknown;
            }
        }

        internal static NativeVideoDynamicRange NormalizeNativeVideoDynamicRange(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoDynamicRange.Sdr;
                case 2:
                    return NativeVideoDynamicRange.Hdr10;
                case 3:
                    return NativeVideoDynamicRange.Hlg;
                case 4:
                    return NativeVideoDynamicRange.DolbyVision;
                default:
                    return NativeVideoDynamicRange.Unknown;
            }
        }

        internal static VideoColorInfoView NormalizeVideoColorInfo(RustAVVideoColorInfo info)
        {
            return new VideoColorInfoView
            {
                Range = NormalizeNativeVideoColorRange(info.Range),
                Matrix = NormalizeNativeVideoColorMatrix(info.Matrix),
                Primaries = NormalizeNativeVideoColorPrimaries(info.Primaries),
                Transfer = NormalizeNativeVideoTransferCharacteristic(info.Transfer),
                BitDepth = info.BitDepth,
                DynamicRange = NormalizeNativeVideoDynamicRange(info.DynamicRange),
            };
        }

        internal static NativeVideoPlaneTextureFormat NormalizeNativeVideoPlaneTextureFormat(
            int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoPlaneTextureFormat.R8Unorm;
                case 2:
                    return NativeVideoPlaneTextureFormat.Rg8Unorm;
                case 3:
                    return NativeVideoPlaneTextureFormat.R16Unorm;
                case 4:
                    return NativeVideoPlaneTextureFormat.Rg16Unorm;
                default:
                    return NativeVideoPlaneTextureFormat.Unknown;
            }
        }

        internal static NativeVideoPlaneResourceKind NormalizeNativeVideoPlaneResourceKind(
            int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoPlaneResourceKind.D3D11Texture2D;
                case 2:
                    return NativeVideoPlaneResourceKind.D3D11ShaderResourceView;
                default:
                    return NativeVideoPlaneResourceKind.Unknown;
            }
        }

        internal static NativeVideoPixelFormatKind ToPublicNativeVideoPixelFormat(
            NativeVideoPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case NativeVideoPixelFormat.Yuv420p:
                    return NativeVideoPixelFormatKind.Yuv420p;
                case NativeVideoPixelFormat.Rgba32:
                    return NativeVideoPixelFormatKind.Rgba32;
                case NativeVideoPixelFormat.Nv12:
                    return NativeVideoPixelFormatKind.Nv12;
                case NativeVideoPixelFormat.P010:
                    return NativeVideoPixelFormatKind.P010;
                default:
                    return NativeVideoPixelFormatKind.Unknown;
            }
        }

        internal static NativeVideoColorRangeKind ToPublicNativeVideoColorRange(
            NativeVideoColorRange colorRange)
        {
            switch (colorRange)
            {
                case NativeVideoColorRange.Limited:
                    return NativeVideoColorRangeKind.Limited;
                case NativeVideoColorRange.Full:
                    return NativeVideoColorRangeKind.Full;
                default:
                    return NativeVideoColorRangeKind.Unknown;
            }
        }

        internal static NativeVideoColorMatrixKind ToPublicNativeVideoColorMatrix(
            NativeVideoColorMatrix colorMatrix)
        {
            switch (colorMatrix)
            {
                case NativeVideoColorMatrix.Bt601:
                    return NativeVideoColorMatrixKind.Bt601;
                case NativeVideoColorMatrix.Bt709:
                    return NativeVideoColorMatrixKind.Bt709;
                case NativeVideoColorMatrix.Bt2020Ncl:
                    return NativeVideoColorMatrixKind.Bt2020Ncl;
                case NativeVideoColorMatrix.Bt2020Cl:
                    return NativeVideoColorMatrixKind.Bt2020Cl;
                case NativeVideoColorMatrix.Smpte240M:
                    return NativeVideoColorMatrixKind.Smpte240M;
                case NativeVideoColorMatrix.Rgb:
                    return NativeVideoColorMatrixKind.Rgb;
                default:
                    return NativeVideoColorMatrixKind.Unknown;
            }
        }

        internal static NativeVideoColorPrimariesKind ToPublicNativeVideoColorPrimaries(
            NativeVideoColorPrimaries primaries)
        {
            switch (primaries)
            {
                case NativeVideoColorPrimaries.Bt601:
                    return NativeVideoColorPrimariesKind.Bt601;
                case NativeVideoColorPrimaries.Bt709:
                    return NativeVideoColorPrimariesKind.Bt709;
                case NativeVideoColorPrimaries.Bt2020:
                    return NativeVideoColorPrimariesKind.Bt2020;
                case NativeVideoColorPrimaries.DciP3:
                    return NativeVideoColorPrimariesKind.DciP3;
                default:
                    return NativeVideoColorPrimariesKind.Unknown;
            }
        }

        internal static NativeVideoTransferCharacteristicKind ToPublicNativeVideoTransferCharacteristic(
            NativeVideoTransferCharacteristic transfer)
        {
            switch (transfer)
            {
                case NativeVideoTransferCharacteristic.Bt1886:
                    return NativeVideoTransferCharacteristicKind.Bt1886;
                case NativeVideoTransferCharacteristic.Srgb:
                    return NativeVideoTransferCharacteristicKind.Srgb;
                case NativeVideoTransferCharacteristic.Linear:
                    return NativeVideoTransferCharacteristicKind.Linear;
                case NativeVideoTransferCharacteristic.Smpte240M:
                    return NativeVideoTransferCharacteristicKind.Smpte240M;
                case NativeVideoTransferCharacteristic.Pq:
                    return NativeVideoTransferCharacteristicKind.Pq;
                case NativeVideoTransferCharacteristic.Hlg:
                    return NativeVideoTransferCharacteristicKind.Hlg;
                default:
                    return NativeVideoTransferCharacteristicKind.Unknown;
            }
        }

        internal static NativeVideoDynamicRangeKind ToPublicNativeVideoDynamicRange(
            NativeVideoDynamicRange dynamicRange)
        {
            switch (dynamicRange)
            {
                case NativeVideoDynamicRange.Sdr:
                    return NativeVideoDynamicRangeKind.Sdr;
                case NativeVideoDynamicRange.Hdr10:
                    return NativeVideoDynamicRangeKind.Hdr10;
                case NativeVideoDynamicRange.Hlg:
                    return NativeVideoDynamicRangeKind.Hlg;
                case NativeVideoDynamicRange.DolbyVision:
                    return NativeVideoDynamicRangeKind.DolbyVision;
                default:
                    return NativeVideoDynamicRangeKind.Unknown;
            }
        }

        internal static MediaPlayer.NativeVideoColorInfo ToPublicNativeVideoColorInfo(
            VideoColorInfoView colorInfo)
        {
            return new MediaPlayer.NativeVideoColorInfo
            {
                Range = ToPublicNativeVideoColorRange(colorInfo.Range),
                Matrix = ToPublicNativeVideoColorMatrix(colorInfo.Matrix),
                Primaries = ToPublicNativeVideoColorPrimaries(colorInfo.Primaries),
                Transfer = ToPublicNativeVideoTransferCharacteristic(colorInfo.Transfer),
                BitDepth = colorInfo.BitDepth,
                DynamicRange = ToPublicNativeVideoDynamicRange(colorInfo.DynamicRange),
            };
        }

        internal static NativeVideoPlaneTextureFormatKind ToPublicNativeVideoPlaneTextureFormat(
            NativeVideoPlaneTextureFormat textureFormat)
        {
            switch (textureFormat)
            {
                case NativeVideoPlaneTextureFormat.R8Unorm:
                    return NativeVideoPlaneTextureFormatKind.R8Unorm;
                case NativeVideoPlaneTextureFormat.Rg8Unorm:
                    return NativeVideoPlaneTextureFormatKind.Rg8Unorm;
                case NativeVideoPlaneTextureFormat.R16Unorm:
                    return NativeVideoPlaneTextureFormatKind.R16Unorm;
                case NativeVideoPlaneTextureFormat.Rg16Unorm:
                    return NativeVideoPlaneTextureFormatKind.Rg16Unorm;
                default:
                    return NativeVideoPlaneTextureFormatKind.Unknown;
            }
        }

        internal static NativeVideoPlaneResourceKindKind ToPublicNativeVideoPlaneResourceKind(
            NativeVideoPlaneResourceKind resourceKind)
        {
            switch (resourceKind)
            {
                case NativeVideoPlaneResourceKind.D3D11Texture2D:
                    return NativeVideoPlaneResourceKindKind.D3D11Texture2D;
                case NativeVideoPlaneResourceKind.D3D11ShaderResourceView:
                    return NativeVideoPlaneResourceKindKind.D3D11ShaderResourceView;
                default:
                    return NativeVideoPlaneResourceKindKind.Unknown;
            }
        }
    }

    internal static class MediaSourceResolver
    {
        private const string PreparedStreamingAssetsRootName = "RustAVPreparedStreamingAssets";

        internal sealed class PreparedMediaSource
        {
            internal PreparedMediaSource(
                string originalUri,
                string playbackUri,
                bool isRealtimeSource,
                bool isPreparedStreamingAsset)
            {
                OriginalUri = originalUri;
                PlaybackUri = playbackUri;
                IsRealtimeSource = isRealtimeSource;
                IsPreparedStreamingAsset = isPreparedStreamingAsset;
            }

            internal string OriginalUri { get; private set; }

            internal string PlaybackUri { get; private set; }

            internal bool IsRealtimeSource { get; private set; }

            internal bool IsPreparedStreamingAsset { get; private set; }
        }

        internal static bool IsRemoteUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            Uri parsedUri;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsedUri))
            {
                return false;
            }

            if (parsedUri.IsFile)
            {
                return false;
            }

            if (string.Equals(parsedUri.Scheme, "jar", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        internal static IEnumerator PreparePlayableUri(
            string uri,
            Action<string> onResolved,
            Action<Exception> onError)
        {
            yield return PreparePlayableSource(
                uri,
                source => onResolved(source.PlaybackUri),
                onError);
        }

        internal static IEnumerator PreparePlayableSource(
            string uri,
            Action<PreparedMediaSource> onResolved,
            Action<Exception> onError)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                onError(new ArgumentException("媒体地址不能为空。", "uri"));
                yield break;
            }

            if (IsRemoteUri(uri))
            {
                onResolved(
                    new PreparedMediaSource(
                        uri,
                        uri,
                        true,
                        false));
                yield break;
            }

            string absolutePath;
            Exception absolutePathError;
            if (TryResolveAbsolutePath(uri, out absolutePath, out absolutePathError))
            {
                if (absolutePathError != null)
                {
                    onError(absolutePathError);
                }
                else
                {
                    onResolved(
                        new PreparedMediaSource(
                            uri,
                            absolutePath,
                            false,
                            false));
                }
                yield break;
            }

            var streamingAssetSource = ResolveStreamingAssetSource(uri);
            if (File.Exists(streamingAssetSource))
            {
                onResolved(
                    new PreparedMediaSource(
                        uri,
                        Path.GetFullPath(streamingAssetSource),
                        false,
                        false));
                yield break;
            }

            if (!RequiresStagingCopy(streamingAssetSource))
            {
                onError(new FileNotFoundException(streamingAssetSource + " not found."));
                yield break;
            }

            var preparedPath = BuildPreparedStreamingAssetPath(uri);
            if (File.Exists(preparedPath))
            {
                onResolved(
                    new PreparedMediaSource(
                        uri,
                        preparedPath,
                        false,
                        true));
                yield break;
            }
            var preparedDirectory = Path.GetDirectoryName(preparedPath);
            if (!string.IsNullOrEmpty(preparedDirectory))
            {
                Directory.CreateDirectory(preparedDirectory);
            }

            var requestType = Type.GetType(
                "UnityEngine.Networking.UnityWebRequest, UnityEngine.UnityWebRequestModule");
            if (requestType == null)
            {
                onError(
                    new InvalidOperationException(
                        "UnityWebRequest 模块不可用，无法准备打包媒体资源。"));
                yield break;
            }

            var getMethod = requestType.GetMethod("Get", new[] { typeof(string) });
            var sendWebRequestMethod = requestType.GetMethod("SendWebRequest", Type.EmptyTypes);
            var resultProperty = requestType.GetProperty("result");
            var errorProperty = requestType.GetProperty("error");
            var downloadHandlerProperty = requestType.GetProperty("downloadHandler");
            var disposeMethod = requestType.GetMethod("Dispose", Type.EmptyTypes);
            var successValue = resultProperty != null
                ? Enum.Parse(resultProperty.PropertyType, "Success")
                : null;

            if (getMethod == null
                || sendWebRequestMethod == null
                || resultProperty == null
                || errorProperty == null
                || downloadHandlerProperty == null
                || successValue == null)
            {
                onError(
                    new InvalidOperationException(
                        "UnityWebRequest 反射接口不完整，无法准备打包媒体资源。"));
                yield break;
            }

            var request = getMethod.Invoke(null, new object[] { streamingAssetSource });
            if (request == null)
            {
                onError(
                    new InvalidOperationException(
                        "UnityWebRequest.Get 返回空对象。"));
                yield break;
            }

            try
            {
                var asyncOperation = sendWebRequestMethod.Invoke(request, null);
                if (asyncOperation != null)
                {
                    yield return asyncOperation;
                }

                var resultValue = resultProperty.GetValue(request, null);
                if (!Equals(resultValue, successValue))
                {
                    var errorMessage = errorProperty.GetValue(request, null) as string;
                    onError(
                        new IOException(
                            "Failed to load packaged media: "
                            + streamingAssetSource
                            + " error="
                            + errorMessage));
                    yield break;
                }

                var downloadHandler = downloadHandlerProperty.GetValue(request, null);
                if (downloadHandler == null)
                {
                    onError(
                        new IOException(
                            "UnityWebRequest 缺少 DownloadHandler。"));
                    yield break;
                }

                var dataProperty = downloadHandler.GetType().GetProperty("data");
                var data = dataProperty != null
                    ? dataProperty.GetValue(downloadHandler, null) as byte[]
                    : null;
                if (data == null || data.Length == 0)
                {
                    onError(
                        new IOException(
                            "打包媒体资源下载结果为空。"));
                    yield break;
                }

                File.WriteAllBytes(preparedPath, data);
            }
            finally
            {
                var disposable = request as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
                else if (disposeMethod != null)
                {
                    disposeMethod.Invoke(request, null);
                }
            }

            onResolved(
                new PreparedMediaSource(
                    uri,
                    preparedPath,
                    false,
                    true));
        }

        private static bool TryResolveAbsolutePath(
            string uri,
            out string resolvedPath,
            out Exception error)
        {
            resolvedPath = string.Empty;
            error = null;

            if (Path.IsPathRooted(uri))
            {
                resolvedPath = Path.GetFullPath(uri);
                if (!File.Exists(resolvedPath))
                {
                    error = new FileNotFoundException(resolvedPath + " not found.");
                }
                return true;
            }

            Uri parsedUri;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsedUri))
            {
                return false;
            }

            if (!parsedUri.IsFile)
            {
                return false;
            }

            resolvedPath = parsedUri.LocalPath;
            if (!File.Exists(resolvedPath))
            {
                error = new FileNotFoundException(resolvedPath + " not found.");
            }
            return true;
        }

        private static string ResolveStreamingAssetSource(string uri)
        {
            Uri parsedUri;
            if (Uri.TryCreate(uri, UriKind.Absolute, out parsedUri)
                && string.Equals(parsedUri.Scheme, "jar", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            return CombineStreamingAssetsUri(uri);
        }

        private static string CombineStreamingAssetsUri(string uri)
        {
            var normalizedRelativePath = NormalizeRelativePath(uri);
            Uri parsedRoot;
            if (Uri.TryCreate(Application.streamingAssetsPath, UriKind.Absolute, out parsedRoot)
                && !parsedRoot.IsFile)
            {
                var root = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/');
                return root + "/" + EncodeRelativeUriPath(normalizedRelativePath);
            }

            return Path.GetFullPath(
                Path.Combine(
                    Application.streamingAssetsPath,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool RequiresStagingCopy(string sourcePath)
        {
            Uri parsedUri;
            if (!Uri.TryCreate(sourcePath, UriKind.Absolute, out parsedUri))
            {
                return false;
            }

            return !parsedUri.IsFile;
        }

        private static string BuildPreparedStreamingAssetPath(string uri)
        {
            var normalizedRelativePath = NormalizeRelativePath(uri);
            var preparedRoot = Path.GetFullPath(
                Path.Combine(
                    Application.persistentDataPath,
                    PreparedStreamingAssetsRootName,
                    BuildPreparedSourceNamespaceKey()));
            var preparedPath = Path.GetFullPath(
                Path.Combine(
                    preparedRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!preparedPath.StartsWith(preparedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "StreamingAssets 路径非法，包含越界段。");
            }

            return preparedPath;
        }

        private static string EncodeRelativeUriPath(string relativePath)
        {
            var segments = relativePath.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                segments[index] = Uri.EscapeDataString(
                    Uri.UnescapeDataString(segments[index]));
            }

            return string.Join("/", segments);
        }

        private static string BuildPreparedSourceNamespaceKey()
        {
            var identity = string.Join(
                "|",
                Application.identifier ?? string.Empty,
                Application.version ?? string.Empty,
                Application.streamingAssetsPath ?? string.Empty);

            unchecked
            {
                ulong hash = 1469598103934665603UL;
                for (var index = 0; index < identity.Length; index++)
                {
                    hash ^= identity[index];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16");
            }
        }

        private static string NormalizeRelativePath(string uri)
        {
            var normalized = uri.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("媒体地址不能为空。", "uri");
            }

            if (normalized.Contains("../") || normalized.Contains("..\\"))
            {
                throw new InvalidOperationException(
                    "媒体地址不能包含父目录跳转。");
            }

            return normalized;
        }
    }
}
