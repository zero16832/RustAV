using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 供人工预览使用的 NativeVideo 场景驱动。
    /// 不做验证断言，不自动退出，只负责读取命令行参数并配置窗口/视图。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class CodexNativeVideoPreviewDriver : MonoBehaviour
    {
        public MediaPlayer Player;
        public Transform VideoSurface;
        public Camera PreviewCamera;
        public bool ForceWindowedMode = true;
        public bool ForceExclusiveFullScreen;
        public bool DefaultLoop = true;
        public float LogIntervalSeconds = 1f;
        public bool TraceNativeVideoCadence = false;
        public int PreviewTargetFrameRate = 60;
        public int PreviewVSyncCount = 0;
        public int PreferredRefreshRate = 0;
        public string UriArgumentName = "-uri=";
        public string BackendArgumentName = "-backend=";
        public string WindowWidthArgumentName = "-windowWidth=";
        public string WindowHeightArgumentName = "-windowHeight=";
        public string LoopArgumentName = "-loop=";
        public string PreferRenderEventPassArgumentName = "-preferRenderEventPass=";
        public string PreferUnityNv12DirectShaderArgumentName = "-preferUnityNv12DirectShader=";
        public string PreferUnityNv12ComputeArgumentName = "-preferUnityNv12Compute=";
        public string LogIntervalSecondsArgumentName = "-logIntervalSeconds=";
        public string TraceNativeVideoCadenceArgumentName = "-traceNativeVideoCadence=";
        public string TargetFrameRateArgumentName = "-targetFrameRate=";
        public string VSyncCountArgumentName = "-vSyncCount=";
        public string FullScreenExclusiveArgumentName = "-fullScreenExclusive=";
        public string RefreshRateArgumentName = "-refreshRate=";

        private bool _windowConfigured;
        private bool _hasExplicitWindowWidth;
        private bool _hasExplicitWindowHeight;
        private float _lastDiagnosticRealtime;
        private int _updateCount;
        private float _updateDeltaTimeSum;
        private float _updateDeltaTimeMin = float.PositiveInfinity;
        private float _updateDeltaTimeMax;
        private float _updateUnscaledDeltaTimeSum;
        private float _updateUnscaledDeltaTimeMin = float.PositiveInfinity;
        private float _updateUnscaledDeltaTimeMax;
        private long _lastAcquireCount = -1;
        private long _lastDirectShaderBindCount = -1;
        private long _lastFrameIndex = -1;
        private double _lastFrameTimeSec = -1.0;

        private void Awake()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayer>();
            }

            if (Player == null)
            {
                Debug.LogError("[CodexNativePreview] missing MediaPlayer");
                return;
            }

            var overrideUri = TryReadStringArgument(UriArgumentName);
            if (!string.IsNullOrEmpty(overrideUri))
            {
                Player.Uri = overrideUri;
                Debug.Log("[CodexNativePreview] override uri=" + overrideUri);
            }

            var overrideBackend = TryReadStringArgument(BackendArgumentName);
            MediaBackendKind parsedBackend;
            if (TryParseBackend(overrideBackend, out parsedBackend))
            {
                Player.PreferredBackend = parsedBackend;
                Player.StrictBackend = parsedBackend != MediaBackendKind.Auto;
                Debug.Log(
                    "[CodexNativePreview] override backend=" + parsedBackend
                    + " strict=" + Player.StrictBackend);
            }

            var windowWidth = TryReadIntArgument(WindowWidthArgumentName, Player.Width);
            var windowHeight = TryReadIntArgument(WindowHeightArgumentName, Player.Height);
            if (windowWidth > 0)
            {
                Player.Width = windowWidth;
            }

            if (windowHeight > 0)
            {
                Player.Height = windowHeight;
            }

            var preferRenderEventPass = TryReadBoolArgument(
                PreferRenderEventPassArgumentName,
                Player.PreferNativeVideoRenderEventPass);
            var preferDirectShader = TryReadBoolArgument(
                PreferUnityNv12DirectShaderArgumentName,
                Player.PreferNativeVideoUnityDirectShader);
            var preferCompute = TryReadBoolArgument(
                PreferUnityNv12ComputeArgumentName,
                Player.PreferNativeVideoUnityCompute);
            var loop = TryReadBoolArgument(LoopArgumentName, DefaultLoop);
            LogIntervalSeconds = Mathf.Max(
                0.05f,
                TryReadFloatArgument(LogIntervalSecondsArgumentName, LogIntervalSeconds));
            TraceNativeVideoCadence = TryReadBoolArgument(
                TraceNativeVideoCadenceArgumentName,
                TraceNativeVideoCadence);
            PreviewTargetFrameRate = TryReadIntArgument(
                TargetFrameRateArgumentName,
                PreviewTargetFrameRate);
            PreviewVSyncCount = Mathf.Clamp(
                TryReadIntArgument(VSyncCountArgumentName, PreviewVSyncCount),
                0,
                4);
            ForceExclusiveFullScreen = TryReadBoolArgument(
                FullScreenExclusiveArgumentName,
                ForceExclusiveFullScreen);
            PreferredRefreshRate = Mathf.Max(
                0,
                TryReadIntArgument(RefreshRateArgumentName, PreferredRefreshRate));

            Player.PreferNativeVideoRenderEventPass = preferRenderEventPass;
            Player.PreferNativeVideoUnityDirectShader = preferDirectShader;
            Player.PreferNativeVideoUnityCompute = preferCompute;
            Player.Loop = loop;
            Player.TraceNativeVideoCadence = TraceNativeVideoCadence;

            Debug.Log(
                "[CodexNativePreview] config"
                + " uri=" + Player.Uri
                + " backend=" + Player.PreferredBackend
                + " strict=" + Player.StrictBackend
                + " loop=" + Player.Loop
                + " log_interval_seconds=" + LogIntervalSeconds.ToString("F2")
                + " trace_native_video_cadence=" + TraceNativeVideoCadence
                + " prefer_render_event_pass=" + Player.PreferNativeVideoRenderEventPass
                + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                + " target_frame_rate=" + PreviewTargetFrameRate
                + " v_sync_count=" + PreviewVSyncCount
                + " exclusive_full_screen=" + ForceExclusiveFullScreen
                + " preferred_refresh_rate=" + PreferredRefreshRate
                + " requested_size=" + Player.Width + "x" + Player.Height);
        }

        private void Start()
        {
            if (Player == null)
            {
                return;
            }

            Application.runInBackground = true;
            QualitySettings.vSyncCount = PreviewVSyncCount;
            Application.targetFrameRate = PreviewTargetFrameRate;
            _updateDeltaTimeSum = 0f;
            _updateDeltaTimeMin = float.PositiveInfinity;
            _updateDeltaTimeMax = 0f;
            _updateUnscaledDeltaTimeSum = 0f;
            _updateUnscaledDeltaTimeMin = float.PositiveInfinity;
            _updateUnscaledDeltaTimeMax = 0f;
            _lastDiagnosticRealtime = Time.realtimeSinceStartup;
            Debug.Log(
                "[CodexNativePreview] frame_pacing targetFrameRate="
                + Application.targetFrameRate
                + " vSyncCount="
                + QualitySettings.vSyncCount);

            if (ForceExclusiveFullScreen)
            {
                Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                Screen.fullScreen = true;
                Screen.SetResolution(
                    Player.Width,
                    Player.Height,
                    FullScreenMode.ExclusiveFullScreen,
                    PreferredRefreshRate);
                ConfigureView(Player.Width, Player.Height);
                Debug.Log(
                    "[CodexNativePreview] full_screen_mode=ExclusiveFullScreen"
                    + " refresh_rate=" + PreferredRefreshRate);
            }
            else if (ForceWindowedMode)
            {
                Screen.fullScreen = false;
                Screen.fullScreenMode = FullScreenMode.Windowed;
                Screen.SetResolution(Player.Width, Player.Height, false);
                ConfigureView(Player.Width, Player.Height);
            }
        }

        private void Update()
        {
            _updateCount += 1;
            var deltaTime = Time.deltaTime;
            var unscaledDeltaTime = Time.unscaledDeltaTime;
            _updateDeltaTimeSum += deltaTime;
            _updateDeltaTimeMin = Math.Min(_updateDeltaTimeMin, deltaTime);
            _updateDeltaTimeMax = Math.Max(_updateDeltaTimeMax, deltaTime);
            _updateUnscaledDeltaTimeSum += unscaledDeltaTime;
            _updateUnscaledDeltaTimeMin = Math.Min(_updateUnscaledDeltaTimeMin, unscaledDeltaTime);
            _updateUnscaledDeltaTimeMax = Math.Max(_updateUnscaledDeltaTimeMax, unscaledDeltaTime);
            TryConfigureWindow();
            EmitDiagnosticsIfDue();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log("[CodexNativePreview] escape_pressed quit");
                Application.Quit(0);
            }
        }

        private void EmitDiagnosticsIfDue()
        {
            if (Player == null)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            if (_lastDiagnosticRealtime <= 0f)
            {
                _lastDiagnosticRealtime = now;
                return;
            }

            var elapsed = now - _lastDiagnosticRealtime;
            if (elapsed < LogIntervalSeconds)
            {
                return;
            }

            var updateFps = _updateCount / Mathf.Max(0.001f, elapsed);
            var updateDeltaTimeAvg = _updateCount > 0 ? _updateDeltaTimeSum / _updateCount : 0f;
            var updateUnscaledDeltaTimeAvg = _updateCount > 0
                ? _updateUnscaledDeltaTimeSum / _updateCount
                : 0f;
            var acquireCount = Player.NativeVideoFrameAcquireCount;
            var directShaderBindCount = Player.NativeVideoDirectShaderBindCount;
            var acquireFps = _lastAcquireCount >= 0
                ? (acquireCount - _lastAcquireCount) / Mathf.Max(0.001f, elapsed)
                : 0f;
            var directShaderBindFps = _lastDirectShaderBindCount >= 0
                ? (directShaderBindCount - _lastDirectShaderBindCount) / Mathf.Max(0.001f, elapsed)
                : 0f;

            var hasFrameInfo = Player.TryGetLastNativeVideoFrameInfo(out var frameInfo);
            var frameIndexFps = 0f;
            var presentedTimeFps = 0f;
            if (hasFrameInfo && _lastFrameIndex >= 0)
            {
                frameIndexFps = (frameInfo.FrameIndex - _lastFrameIndex) / Mathf.Max(0.001f, elapsed);
            }

            if (hasFrameInfo && _lastFrameTimeSec >= 0.0)
            {
                var frameTimeDelta = frameInfo.TimeSec - _lastFrameTimeSec;
                if (frameTimeDelta > 0.0)
                {
                    presentedTimeFps = (float)(frameTimeDelta / elapsed);
                }
            }

            var audioSource = Player.GetComponent<AudioSource>();
            var audioSourcePresent = audioSource != null;
            var audioPlaying = audioSourcePresent && audioSource.isPlaying;
            var audioTimeSamples = audioSourcePresent ? audioSource.timeSamples : -1;
            MediaPlayer.NativeVideoFrameCadenceSnapshot cadenceSnapshot;
            var hasCadenceSnapshot = Player.TryTakeNativeVideoFrameCadenceSnapshot(out cadenceSnapshot);
            MediaPlayer.NativeVideoUpdateTimingSnapshot updateTimingSnapshot;
            var hasUpdateTimingSnapshot = Player.TryTakeNativeVideoUpdateTimingSnapshot(out updateTimingSnapshot);
            MediaPlayer.NativeVideoPresentationTelemetrySnapshot presentationSnapshot;
            var hasPresentationSnapshot = Player.TryTakeNativeVideoPresentationTelemetrySnapshot(
                out presentationSnapshot);
            MediaPlayer.PlayerRuntimeHealth healthSnapshot;
            var hasHealthSnapshot = Player.TryGetRuntimeHealth(out healthSnapshot);
            var attemptFps = cadenceSnapshot.AcquireAttemptCount / Mathf.Max(0.001f, elapsed);
            var presentedFps = cadenceSnapshot.PresentCount / Mathf.Max(0.001f, elapsed);
            var acquireMissFps = cadenceSnapshot.AcquireMissCount / Mathf.Max(0.001f, elapsed);
            var healthCurrentMinusFrameMs = hasHealthSnapshot && hasFrameInfo
                ? (healthSnapshot.CurrentTimeSec - frameInfo.TimeSec) * 1000.0
                : 0.0;

            Debug.Log(
                "[CodexNativePreview] cadence"
                + " update_fps=" + updateFps.ToString("F1")
                + " update_delta_ms_avg=" + (updateDeltaTimeAvg * 1000.0f).ToString("F1")
                + " update_delta_ms_min=" + ((_updateDeltaTimeMin == float.PositiveInfinity ? 0f : _updateDeltaTimeMin) * 1000.0f).ToString("F1")
                + " update_delta_ms_max=" + (_updateDeltaTimeMax * 1000.0f).ToString("F1")
                + " update_unscaled_delta_ms_avg=" + (updateUnscaledDeltaTimeAvg * 1000.0f).ToString("F1")
                + " update_unscaled_delta_ms_min=" + ((_updateUnscaledDeltaTimeMin == float.PositiveInfinity ? 0f : _updateUnscaledDeltaTimeMin) * 1000.0f).ToString("F1")
                + " update_unscaled_delta_ms_max=" + (_updateUnscaledDeltaTimeMax * 1000.0f).ToString("F1")
                + " acquire_fps=" + acquireFps.ToString("F1")
                + " acquire_attempt_fps=" + attemptFps.ToString("F1")
                + " acquire_miss_fps=" + acquireMissFps.ToString("F1")
                + " presented_fps=" + presentedFps.ToString("F1")
                + " direct_shader_bind_fps=" + directShaderBindFps.ToString("F1")
                + " frame_index_fps=" + frameIndexFps.ToString("F1")
                + " presented_time_ratio=" + presentedTimeFps.ToString("F3")
                + " acquire_attempt_count=" + cadenceSnapshot.AcquireAttemptCount
                + " acquire_miss_count=" + cadenceSnapshot.AcquireMissCount
                + " presented_count=" + cadenceSnapshot.PresentCount
                + " cadence_samples=" + cadenceSnapshot.SampleCount
                + " cadence_duplicates=" + cadenceSnapshot.DuplicateCount
                + " cadence_duplicate_streak_max=" + cadenceSnapshot.MaxDuplicateStreak
                + " cadence_acquire_miss_streak_max=" + cadenceSnapshot.MaxAcquireMissStreak
                + " cadence_skipped=" + cadenceSnapshot.SkippedFrameCount
                + " cadence_non_monotonic=" + cadenceSnapshot.NonMonotonicCount
                + " cadence_last_index_delta=" + cadenceSnapshot.LastFrameIndexDelta
                + " cadence_last_time_delta_ms="
                + (cadenceSnapshot.LastFrameTimeDeltaSec * 1000.0).ToString("F1")
                + " cadence_last_realtime_delta_ms="
                + (cadenceSnapshot.LastRealtimeDeltaSec * 1000.0).ToString("F1")
                + " cadence_time_delta_ms_min="
                + (cadenceSnapshot.MinFrameTimeDeltaSec * 1000.0).ToString("F1")
                + " cadence_time_delta_ms_avg="
                + (cadenceSnapshot.AvgFrameTimeDeltaSec * 1000.0).ToString("F1")
                + " cadence_time_delta_ms_max="
                + (cadenceSnapshot.MaxFrameTimeDeltaSec * 1000.0).ToString("F1")
                + " cadence_realtime_delta_ms_min="
                + (cadenceSnapshot.MinRealtimeDeltaSec * 1000.0).ToString("F1")
                + " cadence_realtime_delta_ms_avg="
                + (cadenceSnapshot.AvgRealtimeDeltaSec * 1000.0).ToString("F1")
                + " cadence_realtime_delta_ms_max="
                + (cadenceSnapshot.MaxRealtimeDeltaSec * 1000.0).ToString("F1")
                + " presentation_failure_count=" + cadenceSnapshot.PresentationFailureCount
                + " path_render_event_pass_count=" + cadenceSnapshot.RenderEventPassCount
                + " path_direct_bind_count=" + cadenceSnapshot.DirectBindCount
                + " path_direct_shader_count=" + cadenceSnapshot.DirectShaderCount
                + " path_compute_count=" + cadenceSnapshot.ComputeCount
                + " native_update_count=" + (hasUpdateTimingSnapshot ? updateTimingSnapshot.UpdateCount : 0)
                + " native_update_player_ms_avg=" + (hasUpdateTimingSnapshot ? updateTimingSnapshot.UpdatePlayerElapsedMsAvg.ToString("F2") : "0.00")
                + " native_update_player_ms_max=" + (hasUpdateTimingSnapshot ? updateTimingSnapshot.UpdatePlayerElapsedMsMax.ToString("F2") : "0.00")
                + " native_update_video_ms_avg=" + (hasUpdateTimingSnapshot ? updateTimingSnapshot.UpdateNativeVideoFrameElapsedMsAvg.ToString("F2") : "0.00")
                + " native_update_video_ms_max=" + (hasUpdateTimingSnapshot ? updateTimingSnapshot.UpdateNativeVideoFrameElapsedMsMax.ToString("F2") : "0.00")
                + " native_update_audio_ms_avg=" + (hasUpdateTimingSnapshot ? updateTimingSnapshot.UpdateAudioBufferElapsedMsAvg.ToString("F2") : "0.00")
                + " native_update_audio_ms_max=" + (hasUpdateTimingSnapshot ? updateTimingSnapshot.UpdateAudioBufferElapsedMsMax.ToString("F2") : "0.00")
                + " presentation_snapshot_available=" + hasPresentationSnapshot
                + " health_snapshot_available=" + hasHealthSnapshot
                + " health_current_time_sec=" + (hasHealthSnapshot ? healthSnapshot.CurrentTimeSec.ToString("F3") : "-1.000")
                + " health_current_minus_frame_ms=" + healthCurrentMinusFrameMs.ToString("F1")
                + " presentation_render_event_pass_attempt_count="
                + (hasPresentationSnapshot ? presentationSnapshot.RenderEventPassAttemptCount : 0)
                + " presentation_render_event_pass_success_count="
                + (hasPresentationSnapshot ? presentationSnapshot.RenderEventPassSuccessCount : 0)
                + " presentation_direct_bind_attempt_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectBindAttemptCount : 0)
                + " presentation_direct_bind_success_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectBindSuccessCount : 0)
                + " presentation_direct_shader_attempt_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderAttemptCount : 0)
                + " presentation_direct_shader_success_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderSuccessCount : 0)
                + " presentation_direct_shader_source_plane_textures_unsupported_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderSourcePlaneTexturesUnsupportedCount : 0)
                + " presentation_direct_shader_shader_unavailable_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderShaderUnavailableCount : 0)
                + " presentation_direct_shader_acquire_source_plane_textures_failure_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderAcquireSourcePlaneTexturesFailureCount : 0)
                + " presentation_direct_shader_plane_textures_usability_failure_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderPlaneTexturesUsabilityFailureCount : 0)
                + " presentation_direct_shader_material_failure_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderMaterialFailureCount : 0)
                + " presentation_direct_shader_exception_count="
                + (hasPresentationSnapshot ? presentationSnapshot.DirectShaderExceptionCount : 0)
                + " presentation_compute_attempt_count="
                + (hasPresentationSnapshot ? presentationSnapshot.ComputeAttemptCount : 0)
                + " presentation_compute_success_count="
                + (hasPresentationSnapshot ? presentationSnapshot.ComputeSuccessCount : 0)
                + " presentation_compute_source_plane_textures_unsupported_count="
                + (hasPresentationSnapshot ? presentationSnapshot.ComputeSourcePlaneTexturesUnsupportedCount : 0)
                + " presentation_compute_shader_unavailable_count="
                + (hasPresentationSnapshot ? presentationSnapshot.ComputeShaderUnavailableCount : 0)
                + " presentation_compute_acquire_source_plane_textures_failure_count="
                + (hasPresentationSnapshot ? presentationSnapshot.ComputeAcquireSourcePlaneTexturesFailureCount : 0)
                + " presentation_compute_plane_textures_usability_failure_count="
                + (hasPresentationSnapshot ? presentationSnapshot.ComputePlaneTexturesUsabilityFailureCount : 0)
                + " presentation_compute_exception_count="
                + (hasPresentationSnapshot ? presentationSnapshot.ComputeExceptionCount : 0)
                + " presentation_path=" + Player.NativeVideoPresentationPath
                + " native_active=" + Player.IsNativeVideoPathActive
                + " has_frame=" + Player.HasPresentedNativeVideoFrame
                + " frame_index=" + (hasFrameInfo ? frameInfo.FrameIndex : -1)
                + " frame_time=" + (hasFrameInfo ? frameInfo.TimeSec.ToString("F3") : "-1")
                + " audio_source_present=" + audioSourcePresent
                + " audio_playing=" + audioPlaying
                + " audio_time_samples=" + audioTimeSamples
                + " cadence_snapshot_available=" + hasCadenceSnapshot
                + " backend=" + Player.ActualBackendKind);

            _lastDiagnosticRealtime = now;
            _updateCount = 0;
            _updateDeltaTimeSum = 0f;
            _updateDeltaTimeMin = float.PositiveInfinity;
            _updateDeltaTimeMax = 0f;
            _updateUnscaledDeltaTimeSum = 0f;
            _updateUnscaledDeltaTimeMin = float.PositiveInfinity;
            _updateUnscaledDeltaTimeMax = 0f;
            _lastAcquireCount = acquireCount;
            _lastDirectShaderBindCount = directShaderBindCount;
            if (hasFrameInfo)
            {
                _lastFrameIndex = frameInfo.FrameIndex;
                _lastFrameTimeSec = frameInfo.TimeSec;
            }
        }

        private void TryConfigureWindow()
        {
            if (Player == null)
            {
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
                "[CodexNativePreview] window_configured="
                + width + "x" + height
                + " reason=" + source);
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

            if (PreviewCamera != null)
            {
                PreviewCamera.orthographic = true;
                PreviewCamera.orthographicSize = 0.5f;
            }
        }

        private string TryReadStringArgument(string prefix)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (!string.IsNullOrEmpty(argument)
                    && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(prefix.Length);
                }
            }

            return string.Empty;
        }

        private int TryReadIntArgument(string prefix, int fallback)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (!string.IsNullOrEmpty(argument)
                    && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (int.TryParse(argument.Substring(prefix.Length), out parsed))
                    {
                        if (string.Equals(prefix, WindowWidthArgumentName, StringComparison.Ordinal))
                        {
                            _hasExplicitWindowWidth = true;
                        }
                        else if (string.Equals(prefix, WindowHeightArgumentName, StringComparison.Ordinal))
                        {
                            _hasExplicitWindowHeight = true;
                        }
                        return parsed;
                    }
                }
            }

            return fallback;
        }

        private float TryReadFloatArgument(string prefix, float fallback)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (!string.IsNullOrEmpty(argument)
                    && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    float parsed;
                    if (float.TryParse(
                        argument.Substring(prefix.Length),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out parsed))
                    {
                        return parsed;
                    }
                }
            }

            return fallback;
        }

        private bool TryReadBoolArgument(string prefix, bool fallback)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (!string.IsNullOrEmpty(argument)
                    && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    bool parsed;
                    if (bool.TryParse(argument.Substring(prefix.Length), out parsed))
                    {
                        return parsed;
                    }
                }
            }

            return fallback;
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
                    return false;
            }
        }
    }
}
