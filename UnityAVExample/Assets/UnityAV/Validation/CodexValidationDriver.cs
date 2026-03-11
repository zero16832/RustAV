using System;
using System.Collections;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 用于场景级验证的最小运行时驱动。
    /// 它会周期性输出播放时间、纹理和音频状态，并在超时后自动退出。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class CodexValidationDriver : MonoBehaviour
    {
        private const float MinimumPlaybackAdvanceSeconds = 1.0f;

        public MediaPlayerPull Player;
        public float ValidationSeconds = 6f;
        public float StartupTimeoutSeconds = 10f;
        public float LogIntervalSeconds = 1f;
        public float RealtimeReferenceLagToleranceSeconds = 0.10f;
        public string UriArgumentName = "-uri=";
        public string BackendArgumentName = "-backend=";
        public string ValidationSecondsArgumentName = "-validationSeconds=";
        public string StartupTimeoutSecondsArgumentName = "-startupTimeoutSeconds=";
        public string WindowWidthArgumentName = "-windowWidth=";
        public string WindowHeightArgumentName = "-windowHeight=";
        public string PublisherStartUnixMsArgumentName = "-publisherStartUnixMs=";
        public bool ForceWindowedMode = true;
        public Transform VideoSurface;
        public Camera ValidationCamera;

        private float _lastLogTime;
        private float _startTime;
        private int _requestedWindowWidth;
        private int _requestedWindowHeight;
        private bool _hasExplicitWindowWidth;
        private bool _hasExplicitWindowHeight;
        private bool _windowConfigured;
        private bool _sourceSizedWindowApplied;
        private bool _validationWindowStarted;
        private float _validationWindowStartTime;
        private string _validationWindowStartReason = string.Empty;
        private double _validationWindowInitialPlaybackTime = -1.0;
        private double _maxObservedPlaybackTime = -1.0;
        private long _publisherStartUnixMs = -1;
        private bool _hasPublisherStartUnixMs;
        private bool _observedTextureDuringWindow;
        private bool _observedAudioDuringWindow;
        private bool _observedStartedDuringWindow;

        private void Awake()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayerPull>();
            }

            if (Player == null)
            {
                return;
            }

            var overrideUri = TryReadStringArgument(UriArgumentName);
            if (!string.IsNullOrEmpty(overrideUri))
            {
                Player.Uri = overrideUri;
                Debug.Log("[CodexValidation] override uri=" + overrideUri);
            }

            var overrideBackend = TryReadStringArgument(BackendArgumentName);
            MediaBackendKind parsedBackend;
            if (TryParseBackend(overrideBackend, out parsedBackend))
            {
                Player.PreferredBackend = parsedBackend;
                Player.StrictBackend = parsedBackend != MediaBackendKind.Auto;
                Debug.Log(
                    "[CodexValidation] override backend=" + parsedBackend
                    + " strict=" + Player.StrictBackend);
            }

            ValidationSeconds = TryReadFloatArgument(
                ValidationSecondsArgumentName,
                ValidationSeconds);
            StartupTimeoutSeconds = TryReadFloatArgument(
                StartupTimeoutSecondsArgumentName,
                StartupTimeoutSeconds);
            _publisherStartUnixMs = TryReadLongArgument(
                PublisherStartUnixMsArgumentName,
                -1L,
                out _hasPublisherStartUnixMs);

            _requestedWindowWidth = TryReadIntArgument(WindowWidthArgumentName, Player.Width, out _hasExplicitWindowWidth);
            _requestedWindowHeight = TryReadIntArgument(WindowHeightArgumentName, Player.Height, out _hasExplicitWindowHeight);

            if (_requestedWindowWidth > 0)
            {
                Player.Width = _requestedWindowWidth;
            }

            if (_requestedWindowHeight > 0)
            {
                Player.Height = _requestedWindowHeight;
            }
        }

        private void Start()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayerPull>();
            }

            if (Player == null)
            {
                Debug.LogError("[CodexValidation] missing MediaPlayerPull");
                StartCoroutine(QuitAfterDelay(1f, 2));
                return;
            }

            // 场景验证需要在无人值守运行时维持稳定时钟，避免失焦后被系统节流。
            Application.runInBackground = true;
            Debug.Log("[CodexValidation] runInBackground=True");

            _lastLogTime = Time.realtimeSinceStartup;
            _startTime = _lastLogTime;
            Debug.Log(
                string.Format(
                    "[CodexValidation] start validation seconds={0:F1} requestedWindow={1}x{2} explicitWindow={3}",
                    ValidationSeconds,
                    Player.Width,
                    Player.Height,
                    HasExplicitWindowOverride()));

            if (HasExplicitWindowOverride())
            {
                ConfigureWindow(Player.Width, Player.Height, "explicit-override");
                _windowConfigured = true;
            }
            StartCoroutine(RunValidation());
        }

        private IEnumerator RunValidation()
        {
            var startTime = Time.realtimeSinceStartup;
            while (true)
            {
                var now = Time.realtimeSinceStartup;
                var snapshot = CaptureSnapshot();
                if (!_validationWindowStarted)
                {
                    var startupElapsed = now - startTime;
                    var outputsReady = snapshot.HasTexture
                        && (!Player.EnableAudio || snapshot.AudioPlaying);
                    if (outputsReady)
                    {
                        StartValidationWindow(
                            now,
                            startupElapsed,
                            "av-output-start",
                            snapshot.PlaybackTime);
                    }
                    else if (startupElapsed >= StartupTimeoutSeconds)
                    {
                        StartValidationWindow(
                            now,
                            startupElapsed,
                            "startup-timeout",
                            snapshot.PlaybackTime);
                    }
                }
                else
                {
                    RecordValidationObservation(snapshot);
                }

                if (_validationWindowStarted
                    && now - _validationWindowStartTime >= ValidationSeconds)
                {
                    break;
                }

                if (Time.realtimeSinceStartup - _lastLogTime >= LogIntervalSeconds)
                {
                    EmitStatus();
                    _lastLogTime = Time.realtimeSinceStartup;
                }

                yield return null;
            }

            var finalSnapshot = EmitStatus();
            var validationPassed = EvaluateValidationResult(finalSnapshot);
            var exitCode = validationPassed ? 0 : 2;
            yield return QuitAfterDelay(0.5f, exitCode);
        }

        private ValidationSnapshot EmitStatus()
        {
            var snapshot = CaptureSnapshot();

            Debug.Log(string.Format(
                "[CodexValidation] time={0:F3}s texture={1} audioPlaying={2} started={3} startupElapsed={4:F3}s sourceState={5} sourcePackets={6} sourceTimeouts={7} sourceReconnects={8} window={9}x{10} textureSize={11}x{12} fullscreen={13} mode={14} backend={15}",
                snapshot.PlaybackTime,
                snapshot.HasTexture,
                snapshot.AudioPlaying,
                snapshot.Started,
                Player.StartupElapsedSeconds,
                snapshot.SourceState,
                snapshot.SourcePackets,
                snapshot.SourceTimeouts,
                snapshot.SourceReconnects,
                Screen.width,
                Screen.height,
                snapshot.TextureWidth,
                snapshot.TextureHeight,
                Screen.fullScreen,
                Screen.fullScreenMode,
                Player.ActualBackendKind));
            if (snapshot.HasAvSyncSample)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] av_sync delta_ms={0:F1} audio_presented_sec={1:F3} playback_sec={2:F3} audio_pipeline_delay_ms={3:F1}",
                    snapshot.AvSyncDeltaMilliseconds,
                    snapshot.AudioPresentedTimeSec,
                    snapshot.PlaybackTime,
                    snapshot.AudioPipelineDelaySec * 1000.0));
            }
            if (snapshot.HasRealtimeLatencySample)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] realtime_latency latency_ms={0:F1} publisher_elapsed_sec={1:F3} reference_sec={2:F3}",
                    snapshot.RealtimeLatencyMilliseconds,
                    snapshot.PublisherElapsedTimeSec,
                    snapshot.RealtimeReferenceTimeSec));
            }
            if (snapshot.HasRealtimeProbeSample)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] realtime_probe unix_ms={0} reference_sec={1:F3}",
                    snapshot.RealtimeProbeUnixMs,
                    snapshot.RealtimeReferenceTimeSec));
            }

            return snapshot;
        }

        private ValidationSnapshot CaptureSnapshot()
        {
            var playbackTime = SafeReadPlaybackTime();
            var hasTexture = Player.HasPresentedVideoFrame
                && Player.TargetMaterial != null
                && Player.TargetMaterial.mainTexture != null;
            var audioSource = Player.GetComponent<AudioSource>();
            var audioPlaying = audioSource != null && audioSource.isPlaying;
            var textureWidth = hasTexture ? Player.TargetMaterial.mainTexture.width : 0;
            var textureHeight = hasTexture ? Player.TargetMaterial.mainTexture.height : 0;
            double audioPresentedTimeSec;
            double audioPipelineDelaySec;
            var hasAudioPresentation = Player.TryGetEstimatedAudioPresentation(
                out audioPresentedTimeSec,
                out audioPipelineDelaySec);

            MediaPlayerPull.PlayerRuntimeHealth health;
            var hasHealth = Player.TryGetRuntimeHealth(out health);
            double presentedVideoTimeSec;
            var hasPresentedVideoTime = Player.TryGetPresentedVideoTimeSec(out presentedVideoTimeSec);
            var referencePlaybackTime = hasPresentedVideoTime
                ? presentedVideoTimeSec
                : playbackTime;
            if (hasHealth)
            {
                if (referencePlaybackTime < 0.0)
                {
                    referencePlaybackTime = health.CurrentTimeSec;
                }
                else if (health.IsRealtime
                    && health.CurrentTimeSec > referencePlaybackTime + RealtimeReferenceLagToleranceSeconds)
                {
                    referencePlaybackTime = health.CurrentTimeSec;
                }
            }

            var hasAvSyncSample = hasAudioPresentation && referencePlaybackTime >= 0.0;
            var avSyncDeltaMilliseconds = hasAvSyncSample
                ? (audioPresentedTimeSec - referencePlaybackTime) * 1000.0
                : 0.0;
            var hasRealtimeLatencySample = false;
            var realtimeLatencyMilliseconds = 0.0;
            var publisherElapsedTimeSec = 0.0;
            var hasRealtimeProbeSample = false;
            long realtimeProbeUnixMs = 0;
            if (hasHealth
                && health.IsRealtime
                && referencePlaybackTime >= 0.0)
            {
                realtimeProbeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                hasRealtimeProbeSample = true;
            }
            if (_hasPublisherStartUnixMs
                && hasHealth
                && health.IsRealtime
                && referencePlaybackTime >= 0.0)
            {
                var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (nowUnixMs >= _publisherStartUnixMs)
                {
                    publisherElapsedTimeSec =
                        (nowUnixMs - _publisherStartUnixMs) / 1000.0;
                    realtimeLatencyMilliseconds =
                        (publisherElapsedTimeSec - referencePlaybackTime) * 1000.0;
                    hasRealtimeLatencySample = true;
                }
            }

            return new ValidationSnapshot
            {
                PlaybackTime = playbackTime,
                HasTexture = hasTexture,
                AudioPlaying = audioPlaying,
                Started = Player.HasStartedPlayback,
                TextureWidth = textureWidth,
                TextureHeight = textureHeight,
                SourceState = hasHealth ? health.SourceConnectionState.ToString() : "Unavailable",
                SourcePackets = hasHealth ? health.SourcePacketCount.ToString() : "-1",
                SourceTimeouts = hasHealth ? health.SourceTimeoutCount.ToString() : "-1",
                SourceReconnects = hasHealth ? health.SourceReconnectCount.ToString() : "-1",
                HasAvSyncSample = hasAvSyncSample,
                AudioPresentedTimeSec = audioPresentedTimeSec,
                AudioPipelineDelaySec = audioPipelineDelaySec,
                AvSyncDeltaMilliseconds = avSyncDeltaMilliseconds,
                HasRealtimeLatencySample = hasRealtimeLatencySample,
                RealtimeLatencyMilliseconds = realtimeLatencyMilliseconds,
                PublisherElapsedTimeSec = publisherElapsedTimeSec,
                RealtimeReferenceTimeSec = referencePlaybackTime,
                HasRealtimeProbeSample = hasRealtimeProbeSample,
                RealtimeProbeUnixMs = realtimeProbeUnixMs,
            };
        }

        private void StartValidationWindow(
            float now,
            float startupElapsed,
            string reason,
            double playbackTime)
        {
            _validationWindowStarted = true;
            _validationWindowStartTime = now;
            _validationWindowStartReason = reason;
            _validationWindowInitialPlaybackTime = playbackTime;
            _maxObservedPlaybackTime = playbackTime;
            Debug.Log(
                string.Format(
                    "[CodexValidation] validation_window_started reason={0} startup_elapsed={1:F3}s",
                    reason,
                    startupElapsed));
        }

        private void RecordValidationObservation(ValidationSnapshot snapshot)
        {
            _observedTextureDuringWindow |= snapshot.HasTexture;
            _observedAudioDuringWindow |= snapshot.AudioPlaying;
            _observedStartedDuringWindow |= snapshot.Started;
            if (snapshot.PlaybackTime > _maxObservedPlaybackTime)
            {
                _maxObservedPlaybackTime = snapshot.PlaybackTime;
            }
        }

        private bool EvaluateValidationResult(ValidationSnapshot finalSnapshot)
        {
            RecordValidationObservation(finalSnapshot);

            var playbackAdvance = 0.0;
            if (_maxObservedPlaybackTime >= 0.0 && _validationWindowInitialPlaybackTime >= 0.0)
            {
                playbackAdvance = _maxObservedPlaybackTime - _validationWindowInitialPlaybackTime;
            }

            if (_validationWindowStartReason == "startup-timeout"
                && !_observedStartedDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=startup-timeout-no-playback");
                return false;
            }

            if (!_observedStartedDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=playback-not-started");
                return false;
            }

            if (!_observedTextureDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=missing-video-frame");
                return false;
            }

            if (!_observedAudioDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=audio-not-playing");
                return false;
            }

            if (playbackAdvance < MinimumPlaybackAdvanceSeconds)
            {
                Debug.LogError(
                    string.Format(
                        "[CodexValidation] result=failed reason=playback-stalled advance={0:F3}s",
                        playbackAdvance));
                return false;
            }

            Debug.Log(
                string.Format(
                    "[CodexValidation] result=passed reason=steady-playback advance={0:F3}s sourceState={1} sourceTimeouts={2} sourceReconnects={3}",
                    playbackAdvance,
                    finalSnapshot.SourceState,
                    finalSnapshot.SourceTimeouts,
                    finalSnapshot.SourceReconnects));
            Debug.Log("[CodexValidation] complete");
            return true;
        }

        private double SafeReadPlaybackTime()
        {
            try
            {
                return Player.Time();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CodexValidation] time read failed: " + ex.Message);
                return -1.0;
            }
        }

        private void Update()
        {
            TryConfigureWindow();
        }

        private void TryConfigureWindow()
        {
            if (Player == null)
            {
                return;
            }

            int width;
            int height;

            if (Player.TryGetPrimaryVideoSize(out width, out height))
            {
                ConfigureView(width, height);

                if (HasExplicitWindowOverride())
                {
                    _sourceSizedWindowApplied = true;
                    return;
                }

                if (!_sourceSizedWindowApplied || Screen.width != width || Screen.height != height)
                {
                    ConfigureWindow(width, height, "source");
                    ConfigureView(width, height);
                    _windowConfigured = true;
                    _sourceSizedWindowApplied = true;
                }
                return;
            }

            if (_windowConfigured || Player.TargetMaterial == null || HasExplicitWindowOverride())
            {
                return;
            }

            var texture = Player.TargetMaterial.mainTexture;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _startTime < 1.0f)
            {
                return;
            }

            ConfigureWindow(texture.width, texture.height, "texture-fallback");
            ConfigureView(texture.width, texture.height);
            _windowConfigured = true;
        }

        private bool HasExplicitWindowOverride()
        {
            return _hasExplicitWindowWidth || _hasExplicitWindowHeight;
        }

        private void ConfigureWindow(int width, int height, string source)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (ForceWindowedMode)
            {
                Screen.fullScreenMode = FullScreenMode.Windowed;
                Screen.fullScreen = false;
            }

            Screen.SetResolution(width, height, false);
            Debug.Log(
                string.Format(
                    "[CodexValidation] window_configured={0}x{1} reason={2} fullscreen={3} mode={4}",
                    width,
                    height,
                    source,
                    Screen.fullScreen,
                    Screen.fullScreenMode));
        }

        private void ConfigureView(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var aspect = (float)width / height;

            if (VideoSurface != null)
            {
                VideoSurface.localScale = new Vector3(aspect, 1f, 1f);
            }

            if (ValidationCamera != null)
            {
                ValidationCamera.orthographic = true;
                ValidationCamera.orthographicSize = 0.5f;
            }
        }

        private static string TryReadStringArgument(string prefix)
        {
            var args = Environment.GetCommandLineArgs();
            if (args == null)
            {
                return string.Empty;
            }

            foreach (var arg in args)
            {
                if (arg != null && arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(prefix.Length);
                }
            }

            return string.Empty;
        }

        private static int TryReadIntArgument(string prefix, int fallback, out bool hasExplicitValue)
        {
            var value = TryReadStringArgument(prefix);
            hasExplicitValue = !string.IsNullOrEmpty(value);
            int parsed;
            if (string.IsNullOrEmpty(value) || !int.TryParse(value, out parsed) || parsed <= 0)
            {
                return fallback;
            }

            return parsed;
        }

        private static float TryReadFloatArgument(string prefix, float fallback)
        {
            var value = TryReadStringArgument(prefix);
            float parsed;
            if (string.IsNullOrEmpty(value)
                || !float.TryParse(value, out parsed)
                || parsed <= 0f)
            {
                return fallback;
            }

            return parsed;
        }

        private static long TryReadLongArgument(string prefix, long fallback, out bool hasExplicitValue)
        {
            var value = TryReadStringArgument(prefix);
            hasExplicitValue = !string.IsNullOrEmpty(value);
            long parsed;
            if (string.IsNullOrEmpty(value) || !long.TryParse(value, out parsed))
            {
                return fallback;
            }

            return parsed;
        }

        private static bool TryParseBackend(string rawValue, out MediaBackendKind backend)
        {
            backend = MediaBackendKind.Auto;
            if (string.IsNullOrEmpty(rawValue))
            {
                return false;
            }

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "auto":
                    backend = MediaBackendKind.Auto;
                    return true;
                case "ffmpeg":
                    backend = MediaBackendKind.Ffmpeg;
                    return true;
                case "gstreamer":
                    backend = MediaBackendKind.Gstreamer;
                    return true;
                default:
                    Debug.LogWarning("[CodexValidation] ignore unknown backend=" + rawValue);
                    return false;
            }
        }

        private IEnumerator QuitAfterDelay(float seconds, int exitCode)
        {
            yield return new WaitForSeconds(seconds);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
            Environment.Exit(exitCode);
#endif
        }

        private struct ValidationSnapshot
        {
            public double PlaybackTime;
            public bool HasTexture;
            public bool AudioPlaying;
            public bool Started;
            public int TextureWidth;
            public int TextureHeight;
            public string SourceState;
            public string SourcePackets;
            public string SourceTimeouts;
            public string SourceReconnects;
            public bool HasAvSyncSample;
            public double AudioPresentedTimeSec;
            public double AudioPipelineDelaySec;
            public double AvSyncDeltaMilliseconds;
            public bool HasRealtimeLatencySample;
            public double RealtimeLatencyMilliseconds;
            public double PublisherElapsedTimeSec;
            public double RealtimeReferenceTimeSec;
            public bool HasRealtimeProbeSample;
            public long RealtimeProbeUnixMs;
        }
    }
}
