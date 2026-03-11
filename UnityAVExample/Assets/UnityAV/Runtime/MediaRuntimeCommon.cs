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

    internal static class MediaNativeInteropCommon
    {
        internal const uint RustAVPlayerOpenOptionsVersion = 1u;
        internal const uint RustAVPlayerHealthSnapshotV2Version = 2u;
        internal const int BackendDiagnosticBufferLength = 512;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlayerOpenOptions
        {
            public uint StructSize;
            public uint StructVersion;
            public int BackendKind;
            public int StrictBackend;
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

        internal struct RuntimeHealthView
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
            public double AudioTimeSec;
            public double AudioPresentedTimeSec;
            public double AudioSinkDelaySec;
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
                    SourceConnectionState = NormalizeSourceConnectionState(snapshot.SourceConnectionState),
                    IsConnected = snapshot.IsConnected != 0,
                    IsPlaying = snapshot.IsPlaying != 0,
                    IsRealtime = snapshot.IsRealtime != 0,
                    SourcePacketCount = snapshot.SourcePacketCount,
                    SourceTimeoutCount = snapshot.SourceTimeoutCount,
                    SourceReconnectCount = snapshot.SourceReconnectCount,
                    SourceLastActivityAgeSec = snapshot.SourceLastActivityAgeSec,
                    CurrentTimeSec = snapshot.CurrentTimeSec,
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