using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 用于验证 MediaPlayer NativeVideo 运行时消费链是否真正进入 Unity 生命周期。
    /// 它不强行要求零拷贝，只验证每帧 Acquire/Release、首帧到达和播放时间推进。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class CodexNativeVideoValidationDriver : MonoBehaviour
    {
        private const float MinimumPlaybackAdvanceSeconds = 1.0f;

        public MediaPlayer Player;
        public float ValidationSeconds = 6f;
        public float StartupTimeoutSeconds = 10f;
        public float LogIntervalSeconds = 1f;
        public bool RequireDirectBinding = false;
        public bool PreferRenderEventPass = true;
        public bool PreferUnityNv12DirectShader = true;
        public bool RequireUnityDirectShader;
        public bool PreferUnityNv12Compute = true;
        public bool RequireUnityCompute;
        public bool RequireStrictZeroCopy = true;
        public string UriArgumentName = "-uri=";
        public string BackendArgumentName = "-backend=";
        public string ValidationSecondsArgumentName = "-validationSeconds=";
        public string StartupTimeoutSecondsArgumentName = "-startupTimeoutSeconds=";
        public string WindowWidthArgumentName = "-windowWidth=";
        public string WindowHeightArgumentName = "-windowHeight=";
        public string RequireDirectBindingArgumentName = "-requireDirectBinding=";
        public string PreferRenderEventPassArgumentName = "-preferRenderEventPass=";
        public string PreferUnityNv12DirectShaderArgumentName = "-preferUnityNv12DirectShader=";
        public string RequireUnityDirectShaderArgumentName = "-requireUnityDirectShader=";
        public string PreferUnityNv12ComputeArgumentName = "-preferUnityNv12Compute=";
        public string RequireUnityComputeArgumentName = "-requireUnityCompute=";
        public string RequireStrictZeroCopyArgumentName = "-requireStrictZeroCopy=";
        public bool ForceWindowedMode = true;

        private float _lastLogTime;
        private bool _validationWindowStarted;
        private float _validationWindowStartTime;
        private double _validationWindowInitialPlaybackTime = -1.0;
        private double _maxObservedPlaybackTime = -1.0;
        private bool _observedNativeFrameDuringWindow;
        private bool _observedDirectBindableFrameDuringWindow;
        private bool _observedBindingStateDuringWindow;
        private bool _observedDirectBindingDuringWindow;
        private bool _observedSourcePlaneTexturesDuringWindow;
        private bool _observedDirectShaderPathDuringWindow;
        private bool _observedComputePathDuringWindow;

        private string BackendSummary
        {
            get
            {
                if (Player == null)
                {
                    return " requested_backend=Unknown actual_backend=Unknown";
                }

                return " requested_backend=" + Player.PreferredBackend
                    + " actual_backend=" + Player.ActualBackendKind;
            }
        }

        private void Awake()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayer>();
            }

            if (Player == null)
            {
                return;
            }

            var overrideUri = TryReadStringArgument(UriArgumentName);
            if (!string.IsNullOrEmpty(overrideUri))
            {
                Player.Uri = overrideUri;
                Debug.Log("[CodexNativeValidation] override uri=" + overrideUri);
            }

            var overrideBackend = TryReadStringArgument(BackendArgumentName);
            MediaBackendKind parsedBackend;
            if (TryParseBackend(overrideBackend, out parsedBackend))
            {
                Player.PreferredBackend = parsedBackend;
                Player.StrictBackend = parsedBackend != MediaBackendKind.Auto;
                Debug.Log(
                    "[CodexNativeValidation] override backend=" + parsedBackend
                    + " strict=" + Player.StrictBackend);
            }

            ValidationSeconds = TryReadFloatArgument(
                ValidationSecondsArgumentName,
                ValidationSeconds);
            StartupTimeoutSeconds = TryReadFloatArgument(
                StartupTimeoutSecondsArgumentName,
                StartupTimeoutSeconds);
            bool hasExplicitDirectBindingRequirement;
            RequireDirectBinding = TryReadBoolArgument(
                RequireDirectBindingArgumentName,
                RequireDirectBinding,
                out hasExplicitDirectBindingRequirement);
            if (hasExplicitDirectBindingRequirement)
            {
                Debug.Log(
                    "[CodexNativeValidation] override require_direct_binding="
                    + RequireDirectBinding);
            }

            bool hasExplicitPreferRenderEventPass;
            PreferRenderEventPass = TryReadBoolArgument(
                PreferRenderEventPassArgumentName,
                PreferRenderEventPass,
                out hasExplicitPreferRenderEventPass);
            if (hasExplicitPreferRenderEventPass)
            {
                Debug.Log(
                    "[CodexNativeValidation] override prefer_render_event_pass="
                    + PreferRenderEventPass);
            }
            Player.PreferNativeVideoRenderEventPass = PreferRenderEventPass;

            bool hasExplicitPreferDirectShader;
            PreferUnityNv12DirectShader = TryReadBoolArgument(
                PreferUnityNv12DirectShaderArgumentName,
                PreferUnityNv12DirectShader,
                out hasExplicitPreferDirectShader);
            if (hasExplicitPreferDirectShader)
            {
                Debug.Log(
                    "[CodexNativeValidation] override prefer_unity_nv12_direct_shader="
                    + PreferUnityNv12DirectShader);
            }

            Player.PreferNativeVideoUnityDirectShader = PreferUnityNv12DirectShader;

            bool hasExplicitRequireDirectShader;
            RequireUnityDirectShader = TryReadBoolArgument(
                RequireUnityDirectShaderArgumentName,
                RequireUnityDirectShader,
                out hasExplicitRequireDirectShader);
            if (hasExplicitRequireDirectShader)
            {
                Debug.Log(
                    "[CodexNativeValidation] override require_unity_direct_shader="
                    + RequireUnityDirectShader);
            }

            bool hasExplicitPreferCompute;
            PreferUnityNv12Compute = TryReadBoolArgument(
                PreferUnityNv12ComputeArgumentName,
                PreferUnityNv12Compute,
                out hasExplicitPreferCompute);
            if (hasExplicitPreferCompute)
            {
                Debug.Log(
                    "[CodexNativeValidation] override prefer_unity_nv12_compute="
                    + PreferUnityNv12Compute);
            }

            Player.PreferNativeVideoUnityDirectShader = PreferUnityNv12DirectShader;
            Player.PreferNativeVideoUnityCompute = PreferUnityNv12Compute;

            bool hasExplicitRequireCompute;
            RequireUnityCompute = TryReadBoolArgument(
                RequireUnityComputeArgumentName,
                RequireUnityCompute,
                out hasExplicitRequireCompute);
            if (hasExplicitRequireCompute)
            {
                Debug.Log(
                    "[CodexNativeValidation] override require_unity_compute="
                    + RequireUnityCompute);
            }

            bool hasExplicitRequireStrictZeroCopy;
            RequireStrictZeroCopy = TryReadBoolArgument(
                RequireStrictZeroCopyArgumentName,
                RequireStrictZeroCopy,
                out hasExplicitRequireStrictZeroCopy);
            if (hasExplicitRequireStrictZeroCopy)
            {
                Debug.Log(
                    "[CodexNativeValidation] override require_strict_zero_copy="
                    + RequireStrictZeroCopy);
            }

            if (RequireUnityCompute)
            {
                Player.PreferNativeVideoUnityCompute = true;
                PreferUnityNv12Compute = true;
                if (!hasExplicitDirectBindingRequirement)
                {
                    RequireDirectBinding = false;
                }
            }

            if (RequireUnityDirectShader)
            {
                Player.PreferNativeVideoUnityDirectShader = true;
                PreferUnityNv12DirectShader = true;
                if (!hasExplicitDirectBindingRequirement)
                {
                    RequireDirectBinding = false;
                }
            }

            var requestedWidth = TryReadIntArgument(WindowWidthArgumentName, Player.Width);
            var requestedHeight = TryReadIntArgument(WindowHeightArgumentName, Player.Height);
            if (requestedWidth > 0)
            {
                Player.Width = requestedWidth;
            }

            if (requestedHeight > 0)
            {
                Player.Height = requestedHeight;
            }
        }

        private void Start()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayer>();
            }

            if (Player == null)
            {
                Debug.LogError("[CodexNativeValidation] missing MediaPlayer");
                Debug.LogError(
                    "[CodexNativeValidation] result=failed reason=missing-player"
                    + " require_direct_binding=" + RequireDirectBinding
                    + " prefer_render_event_pass=" + PreferRenderEventPass
                    + " prefer_unity_direct_shader=" + PreferUnityNv12DirectShader
                    + " require_unity_direct_shader=" + RequireUnityDirectShader
                    + " prefer_unity_compute=" + PreferUnityNv12Compute
                    + " require_unity_compute=" + RequireUnityCompute
                    + " require_strict_zero_copy=" + RequireStrictZeroCopy
                    + BackendSummary);
                StartCoroutine(QuitAfterDelay(1f, 2));
                return;
            }

            Application.runInBackground = true;
            Debug.Log("[CodexNativeValidation] runInBackground=True");

            if (ForceWindowedMode)
            {
                Screen.fullScreen = false;
                Screen.SetResolution(Player.Width, Player.Height, false);
            }

            _lastLogTime = Time.realtimeSinceStartup;
            Debug.Log(
                string.Format(
                    "[CodexNativeValidation] start validation seconds={0:F1} requestedWindow={1}x{2} native_video_preferred={3} require_direct_binding={4} prefer_unity_direct_shader={5} require_unity_direct_shader={6} prefer_unity_compute={7} require_unity_compute={8}",
                    ValidationSeconds,
                    Player.Width,
                    Player.Height,
                    Player.PreferNativeVideo,
                    RequireDirectBinding,
                    Player.PreferNativeVideoUnityDirectShader,
                    RequireUnityDirectShader,
                    Player.PreferNativeVideoUnityCompute,
                    RequireUnityCompute)
                + " require_strict_zero_copy=" + RequireStrictZeroCopy
                + " prefer_render_event_pass=" + PreferRenderEventPass);
            StartCoroutine(RunValidation());
        }

        private IEnumerator RunValidation()
        {
            var startRealtime = Time.realtimeSinceStartup;
            while (true)
            {
                var now = Time.realtimeSinceStartup;
                var snapshot = CaptureSnapshot();

                if (!_validationWindowStarted)
                {
                    var startupElapsed = now - startRealtime;
                    if (snapshot.HasNativeFrame)
                    {
                        StartValidationWindow(now, snapshot);
                    }
                    else if (startupElapsed >= StartupTimeoutSeconds)
                    {
                        Debug.LogError(
                            "[CodexNativeValidation] result=failed reason=startup-timeout"
                            + " native_video_active=" + Player.IsNativeVideoPathActive
                            + " acquire_count=" + Player.NativeVideoFrameAcquireCount
                            + " frame_direct_bindable=" + snapshot.FrameDirectBindable
                            + " prefer_unity_direct_shader="
                            + Player.PreferNativeVideoUnityDirectShader
                            + " require_unity_direct_shader=" + RequireUnityDirectShader
                            + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                            + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                            + " require_unity_compute=" + RequireUnityCompute
                            + " source_plane_textures=" + snapshot.HasSourcePlaneTextures
                            + " compute_path_active=" + snapshot.ComputePathActive
                            + " activation_decision=" + snapshot.ActivationDecision
                            + " presentation_path=" + snapshot.PresentationPath
                            + " zero_cpu_copy_active=" + snapshot.ZeroCpuCopyActive
                            + " source_plane_zero_copy_active="
                            + snapshot.SourcePlaneTexturesZeroCopyActive
                            + " strict_zero_copy_active=" + snapshot.StrictZeroCopyActive
                            + " native_texture_bound=" + FormatBindingState(snapshot)
                            + " binding_state_source="
                            + snapshot.NativeTextureBindingStateSource
                            + BackendSummary);
                        Debug.LogError(
                            "[CodexNativeValidation] startup_timeout native_video_active="
                            + Player.IsNativeVideoPathActive
                            + " acquire_count=" + Player.NativeVideoFrameAcquireCount
                            + " frame_direct_bindable=" + snapshot.FrameDirectBindable
                            + " prefer_unity_direct_shader="
                            + Player.PreferNativeVideoUnityDirectShader
                            + " require_unity_direct_shader=" + RequireUnityDirectShader
                            + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                            + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                            + " require_unity_compute=" + RequireUnityCompute
                            + " source_plane_textures=" + snapshot.HasSourcePlaneTextures
                            + " compute_path_active=" + snapshot.ComputePathActive
                            + " activation_decision=" + snapshot.ActivationDecision
                            + " presentation_path=" + snapshot.PresentationPath
                            + " zero_cpu_copy_active=" + snapshot.ZeroCpuCopyActive
                            + " source_plane_zero_copy_active="
                            + snapshot.SourcePlaneTexturesZeroCopyActive
                            + " strict_zero_copy_active=" + snapshot.StrictZeroCopyActive
                            + " native_texture_bound=" + FormatBindingState(snapshot)
                            + " binding_state_source="
                            + snapshot.NativeTextureBindingStateSource);
                        yield return QuitAfterDelay(0.5f, 2);
                        yield break;
                    }
                }
                else
                {
                    RecordObservation(snapshot);
                    if (now - _validationWindowStartTime >= ValidationSeconds)
                    {
                        break;
                    }
                }

                if (now - _lastLogTime >= LogIntervalSeconds)
                {
                    EmitStatus();
                    _lastLogTime = now;
                }

                yield return null;
            }

            var finalSnapshot = EmitStatus();
            var validationPassed = Evaluate(finalSnapshot);
            yield return QuitAfterDelay(0.5f, validationPassed ? 0 : 2);
        }

        private void StartValidationWindow(float now, ValidationSnapshot snapshot)
        {
            _validationWindowStarted = true;
            _validationWindowStartTime = now;
            _validationWindowInitialPlaybackTime = snapshot.PlaybackTime;
            _maxObservedPlaybackTime = snapshot.PlaybackTime;
            RecordObservation(snapshot);
            Debug.Log(
                "[CodexNativeValidation] validation_window_started startup_elapsed="
                + Player.StartupElapsedSeconds.ToString("F3")
                + " playback_time=" + snapshot.PlaybackTime.ToString("F3")
                + " frame_direct_bindable=" + snapshot.FrameDirectBindable
                + " source_plane_textures=" + snapshot.HasSourcePlaneTextures
                + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                + " compute_path_active=" + snapshot.ComputePathActive
                + " activation_decision=" + snapshot.ActivationDecision
                + " presentation_path=" + snapshot.PresentationPath
                + " zero_cpu_copy_active=" + snapshot.ZeroCpuCopyActive
                + " source_plane_zero_copy_active="
                + snapshot.SourcePlaneTexturesZeroCopyActive
                + " strict_zero_copy_active=" + snapshot.StrictZeroCopyActive
                + " native_texture_bound=" + FormatBindingState(snapshot)
                + " binding_state_source=" + snapshot.NativeTextureBindingStateSource);
        }

        private void RecordObservation(ValidationSnapshot snapshot)
        {
            _observedNativeFrameDuringWindow |= snapshot.HasNativeFrame;
            _observedDirectBindableFrameDuringWindow |= snapshot.FrameDirectBindable;
            if (snapshot.HasNativeTextureBindingState)
            {
                _observedBindingStateDuringWindow = true;
                _observedDirectBindingDuringWindow |= snapshot.NativeTextureBound;
            }
            _observedSourcePlaneTexturesDuringWindow |= snapshot.HasSourcePlaneTextures;
            _observedDirectShaderPathDuringWindow |= snapshot.DirectShaderPathActive;
            _observedComputePathDuringWindow |= snapshot.ComputePathActive;

            if (snapshot.PlaybackTime > _maxObservedPlaybackTime)
            {
                _maxObservedPlaybackTime = snapshot.PlaybackTime;
            }
        }

        private static bool EvaluateStrictZeroCopyObserved(ValidationSnapshot snapshot)
        {
            var sourceZeroCopy = HasNativeVideoFrameFlag(
                snapshot.SourceFlags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            var presentedZeroCopy = HasNativeVideoFrameFlag(
                snapshot.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            var presentedCpuFallback = HasNativeVideoFrameFlag(
                snapshot.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagCpuFallback);
            var sourcePlaneTexturesZeroCopy = HasNativeVideoFrameFlag(
                snapshot.SourcePlaneTextureFlags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            var presentedPathStrictZeroCopy =
                string.Equals(
                    snapshot.PresentationPath,
                    MediaPlayer.NativeVideoPresentationPathKind.DirectBind.ToString(),
                    StringComparison.Ordinal)
                || string.Equals(
                    snapshot.PresentationPath,
                    MediaPlayer.NativeVideoPresentationPathKind.RenderEventPass.ToString(),
                    StringComparison.Ordinal);

            var presentedFrameStrictZeroCopy = snapshot.HasNativeSourceFrame
                && snapshot.HasNativeFrame
                && presentedPathStrictZeroCopy
                && sourceZeroCopy
                && presentedZeroCopy
                && !presentedCpuFallback;

            var unityDirectShaderStrictZeroCopy = snapshot.HasNativeSourceFrame
                && sourceZeroCopy
                && snapshot.HasSourcePlaneTextures
                && sourcePlaneTexturesZeroCopy
                && snapshot.HasBoundNativeVideoPlaneTextures
                && snapshot.DirectShaderPathActive
                && !snapshot.ComputePathActive;

            return presentedFrameStrictZeroCopy || unityDirectShaderStrictZeroCopy;
        }

        private ValidationSnapshot EmitStatus()
        {
            var snapshot = CaptureSnapshot();
            var audioSource = Player != null ? Player.GetComponent<AudioSource>() : null;
            var audioSourcePresent = audioSource != null;
            var audioPlaying = audioSourcePresent && audioSource.isPlaying;
            var audioTimeSamples = audioSourcePresent ? audioSource.timeSamples : -1;
            var audioClipFrequency = audioSourcePresent && audioSource.clip != null
                ? audioSource.clip.frequency
                : 0;
            var audioClipChannels = audioSourcePresent && audioSource.clip != null
                ? audioSource.clip.channels
                : 0;
            MediaPlayer.NativeVideoPresentationTelemetrySnapshot presentationSnapshot =
                default(MediaPlayer.NativeVideoPresentationTelemetrySnapshot);
            var hasPresentationSnapshot = false;
            if (Player != null)
            {
                hasPresentationSnapshot = Player.TryTakeNativeVideoPresentationTelemetrySnapshot(
                    out presentationSnapshot);
            }
            var sourceHardwareDecode = HasNativeVideoFrameFlag(
                snapshot.SourceFlags,
                MediaNativeInteropCommon.NativeVideoFrameFlagHardwareDecode);
            var sourceZeroCopy = HasNativeVideoFrameFlag(
                snapshot.SourceFlags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            var presentedHardwareDecode = HasNativeVideoFrameFlag(
                snapshot.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagHardwareDecode);
            var presentedZeroCopy = HasNativeVideoFrameFlag(
                snapshot.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            var presentedCpuFallback = HasNativeVideoFrameFlag(
                snapshot.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagCpuFallback);
            var strictZeroCopyObserved = EvaluateStrictZeroCopyObserved(snapshot);
            Debug.Log(
                "[CodexNativeValidation] time=" + snapshot.PlaybackTime.ToString("F3") + "s"
                + " native_active=" + Player.IsNativeVideoPathActive
                + " has_native_frame=" + snapshot.HasNativeFrame
                + " frame_direct_bindable=" + snapshot.FrameDirectBindable
                + " source_plane_textures=" + snapshot.HasSourcePlaneTextures
                + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                + " compute_path_active=" + snapshot.ComputePathActive
                + " native_texture_bound=" + FormatBindingState(snapshot)
                + " binding_state_source=" + snapshot.NativeTextureBindingStateSource
                + " acquire_count=" + Player.NativeVideoFrameAcquireCount
                + " release_count=" + Player.NativeVideoFrameReleaseCount
                + " frame_index=" + snapshot.FrameIndex
                + " surface=" + snapshot.SurfaceKind
                + " pixel_format=" + snapshot.PixelFormat
                + " handle=0x" + snapshot.NativeHandle.ToString("X")
                + " flags=0x" + snapshot.Flags.ToString("X")
                + " source_frame=" + snapshot.HasNativeSourceFrame
                + " source_surface=" + snapshot.SourceSurfaceKind
                + " source_pixel_format=" + snapshot.SourcePixelFormat
                + " source_handle=0x" + snapshot.SourceNativeHandle.ToString("X")
                + " source_flags=0x" + snapshot.SourceFlags.ToString("X")
                + " source_plane_texture_flags=0x" + snapshot.SourcePlaneTextureFlags.ToString("X")
                + " source_hwdecode=" + sourceHardwareDecode
                + " source_zero_copy=" + sourceZeroCopy
                + " presented_hwdecode=" + presentedHardwareDecode
                + " presented_zero_copy=" + presentedZeroCopy
                + " presented_cpu_fallback=" + presentedCpuFallback
                + " strict_zero_copy=" + strictZeroCopyObserved
                + " texture=" + snapshot.HasTexture
                + " audio_source_present=" + audioSourcePresent
                + " audio_playing=" + audioPlaying
                + " audio_time_samples=" + audioTimeSamples
                + " audio_clip_hz=" + audioClipFrequency
                + " audio_clip_channels=" + audioClipChannels
                + " presentation_snapshot_available=" + hasPresentationSnapshot
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
                + " backend=" + Player.ActualBackendKind);
            return snapshot;
        }

        private bool Evaluate(ValidationSnapshot snapshot)
        {
            RecordObservation(snapshot);
            var playbackAdvance = _maxObservedPlaybackTime - _validationWindowInitialPlaybackTime;
            if (!_observedNativeFrameDuringWindow)
            {
                Debug.LogError(
                    "[CodexNativeValidation] result=failed reason=no-native-frame"
                    + " require_direct_binding=" + RequireDirectBinding
                    + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                    + " require_unity_direct_shader=" + RequireUnityDirectShader
                    + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                    + " require_unity_compute=" + RequireUnityCompute
                    + " source_plane_textures_supported="
                    + snapshot.SourcePlaneTexturesSupported
                    + " observed_source_plane_textures="
                    + _observedSourcePlaneTexturesDuringWindow
                    + " observed_direct_shader_path="
                    + _observedDirectShaderPathDuringWindow
                    + " observed_compute_path="
                    + _observedComputePathDuringWindow
                    + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                    + " compute_path_active=" + snapshot.ComputePathActive
                    + " source_plane_texture_flags=0x"
                    + snapshot.SourcePlaneTextureFlags.ToString("X")
                    + BackendSummary);
                return false;
            }

            if (!snapshot.HasNativeFrame)
            {
                Debug.LogError(
                    "[CodexNativeValidation] result=failed reason=final-native-frame-missing"
                    + " require_direct_binding=" + RequireDirectBinding
                    + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                    + " require_unity_direct_shader=" + RequireUnityDirectShader
                    + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                    + " require_unity_compute=" + RequireUnityCompute
                    + " source_plane_textures_supported="
                    + snapshot.SourcePlaneTexturesSupported
                    + " observed_source_plane_textures="
                    + _observedSourcePlaneTexturesDuringWindow
                    + " observed_direct_shader_path="
                    + _observedDirectShaderPathDuringWindow
                    + " observed_compute_path="
                    + _observedComputePathDuringWindow
                    + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                    + " compute_path_active=" + snapshot.ComputePathActive
                    + " source_plane_texture_flags=0x"
                    + snapshot.SourcePlaneTextureFlags.ToString("X")
                    + BackendSummary);
                return false;
            }

            if (playbackAdvance < MinimumPlaybackAdvanceSeconds)
            {
                Debug.LogError(
                    "[CodexNativeValidation] result=failed reason=insufficient-playback-advance advance="
                    + playbackAdvance.ToString("F3")
                    + " require_direct_binding=" + RequireDirectBinding
                    + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                    + " require_unity_direct_shader=" + RequireUnityDirectShader
                    + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                    + " require_unity_compute=" + RequireUnityCompute
                    + " source_plane_textures_supported="
                    + snapshot.SourcePlaneTexturesSupported
                    + " observed_source_plane_textures="
                    + _observedSourcePlaneTexturesDuringWindow
                    + " observed_direct_shader_path="
                    + _observedDirectShaderPathDuringWindow
                    + " observed_compute_path="
                    + _observedComputePathDuringWindow
                    + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                    + " compute_path_active=" + snapshot.ComputePathActive
                    + " source_plane_texture_flags=0x"
                    + snapshot.SourcePlaneTextureFlags.ToString("X")
                    + BackendSummary);
                return false;
            }

            if (RequireDirectBinding)
            {
                if (!_observedBindingStateDuringWindow)
                {
                    Debug.LogError(
                        "[CodexNativeValidation] result=failed reason=direct-binding-state-unavailable"
                        + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                        + " require_unity_direct_shader=" + RequireUnityDirectShader
                        + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                        + " require_direct_binding=" + RequireDirectBinding
                        + " require_unity_compute=" + RequireUnityCompute
                        + " observed_frame_direct_bindable="
                        + _observedDirectBindableFrameDuringWindow
                        + " source_plane_textures_supported="
                        + snapshot.SourcePlaneTexturesSupported
                        + " observed_source_plane_textures="
                        + _observedSourcePlaneTexturesDuringWindow
                        + " observed_direct_shader_path="
                        + _observedDirectShaderPathDuringWindow
                        + " observed_compute_path="
                        + _observedComputePathDuringWindow
                        + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                        + " compute_path_active=" + snapshot.ComputePathActive
                        + " source_plane_texture_flags=0x"
                        + snapshot.SourcePlaneTextureFlags.ToString("X")
                        + " pixel_format=" + snapshot.PixelFormat
                        + " source_pixel_format=" + snapshot.SourcePixelFormat
                        + BackendSummary);
                    return false;
                }

                if (!_observedDirectBindingDuringWindow)
                {
                    Debug.LogError(
                        "[CodexNativeValidation] result=failed reason=direct-binding-not-observed"
                        + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                        + " require_unity_direct_shader=" + RequireUnityDirectShader
                        + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                        + " require_direct_binding=" + RequireDirectBinding
                        + " require_unity_compute=" + RequireUnityCompute
                        + " observed_frame_direct_bindable="
                        + _observedDirectBindableFrameDuringWindow
                        + " source_plane_textures_supported="
                        + snapshot.SourcePlaneTexturesSupported
                        + " observed_source_plane_textures="
                        + _observedSourcePlaneTexturesDuringWindow
                        + " observed_direct_shader_path="
                        + _observedDirectShaderPathDuringWindow
                        + " observed_compute_path="
                        + _observedComputePathDuringWindow
                        + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                        + " compute_path_active=" + snapshot.ComputePathActive
                        + " source_plane_texture_flags=0x"
                        + snapshot.SourcePlaneTextureFlags.ToString("X")
                        + " pixel_format=" + snapshot.PixelFormat
                        + " source_pixel_format=" + snapshot.SourcePixelFormat
                        + " final_native_texture_bound=" + FormatBindingState(snapshot)
                        + " binding_state_source="
                        + snapshot.NativeTextureBindingStateSource
                        + BackendSummary);
                    return false;
                }
            }

            if (RequireUnityDirectShader)
            {
                if (!_observedSourcePlaneTexturesDuringWindow)
                {
                    Debug.LogError(
                        "[CodexNativeValidation] result=failed reason=source-plane-textures-not-observed-for-direct-shader"
                        + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                        + " require_unity_direct_shader=" + RequireUnityDirectShader
                        + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                        + " require_direct_binding=" + RequireDirectBinding
                        + " require_unity_compute=" + RequireUnityCompute
                        + " source_plane_textures_supported="
                        + snapshot.SourcePlaneTexturesSupported
                        + " observed_source_plane_textures="
                        + _observedSourcePlaneTexturesDuringWindow
                        + " observed_direct_shader_path="
                        + _observedDirectShaderPathDuringWindow
                        + " observed_compute_path="
                        + _observedComputePathDuringWindow
                        + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                        + " compute_path_active=" + snapshot.ComputePathActive
                        + " source_plane_texture_flags=0x"
                        + snapshot.SourcePlaneTextureFlags.ToString("X")
                        + " pixel_format=" + snapshot.PixelFormat
                        + " source_pixel_format=" + snapshot.SourcePixelFormat
                        + BackendSummary);
                    return false;
                }

                if (!_observedDirectShaderPathDuringWindow)
                {
                    Debug.LogError(
                        "[CodexNativeValidation] result=failed reason=unity-direct-shader-not-observed"
                        + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                        + " require_unity_direct_shader=" + RequireUnityDirectShader
                        + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                        + " require_direct_binding=" + RequireDirectBinding
                        + " require_unity_compute=" + RequireUnityCompute
                        + " source_plane_textures_supported="
                        + snapshot.SourcePlaneTexturesSupported
                        + " observed_source_plane_textures="
                        + _observedSourcePlaneTexturesDuringWindow
                        + " observed_direct_shader_path="
                        + _observedDirectShaderPathDuringWindow
                        + " observed_compute_path="
                        + _observedComputePathDuringWindow
                        + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                        + " compute_path_active=" + snapshot.ComputePathActive
                        + " source_plane_texture_flags=0x"
                        + snapshot.SourcePlaneTextureFlags.ToString("X")
                        + " pixel_format=" + snapshot.PixelFormat
                        + " source_pixel_format=" + snapshot.SourcePixelFormat
                        + BackendSummary);
                    return false;
                }
            }

            if (RequireUnityCompute)
            {
                if (!_observedSourcePlaneTexturesDuringWindow)
                {
                    Debug.LogError(
                        "[CodexNativeValidation] result=failed reason=source-plane-textures-not-observed"
                        + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                        + " require_unity_direct_shader=" + RequireUnityDirectShader
                        + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                        + " require_direct_binding=" + RequireDirectBinding
                        + " require_unity_compute=" + RequireUnityCompute
                        + " source_plane_textures_supported="
                        + snapshot.SourcePlaneTexturesSupported
                        + " observed_source_plane_textures="
                        + _observedSourcePlaneTexturesDuringWindow
                        + " observed_direct_shader_path="
                        + _observedDirectShaderPathDuringWindow
                        + " observed_compute_path="
                        + _observedComputePathDuringWindow
                        + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                        + " compute_path_active=" + snapshot.ComputePathActive
                        + " source_plane_texture_flags=0x"
                        + snapshot.SourcePlaneTextureFlags.ToString("X")
                        + " pixel_format=" + snapshot.PixelFormat
                        + " source_pixel_format=" + snapshot.SourcePixelFormat
                        + BackendSummary);
                    return false;
                }

                if (!_observedComputePathDuringWindow)
                {
                    Debug.LogError(
                        "[CodexNativeValidation] result=failed reason=unity-compute-not-observed"
                        + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                        + " require_unity_direct_shader=" + RequireUnityDirectShader
                        + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                        + " require_direct_binding=" + RequireDirectBinding
                        + " require_unity_compute=" + RequireUnityCompute
                        + " source_plane_textures_supported="
                        + snapshot.SourcePlaneTexturesSupported
                        + " observed_source_plane_textures="
                        + _observedSourcePlaneTexturesDuringWindow
                        + " observed_direct_shader_path="
                        + _observedDirectShaderPathDuringWindow
                        + " observed_compute_path="
                        + _observedComputePathDuringWindow
                        + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                        + " compute_path_active=" + snapshot.ComputePathActive
                        + " source_plane_texture_flags=0x"
                        + snapshot.SourcePlaneTextureFlags.ToString("X")
                        + " pixel_format=" + snapshot.PixelFormat
                        + " source_pixel_format=" + snapshot.SourcePixelFormat
                        + BackendSummary);
                    return false;
                }
            }

            if (RequireStrictZeroCopy && !snapshot.StrictZeroCopyActive)
            {
                Debug.LogError(
                    "[CodexNativeValidation] result=failed reason=strict-zero-copy-not-active"
                    + " require_direct_binding=" + RequireDirectBinding
                    + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                    + " require_unity_direct_shader=" + RequireUnityDirectShader
                    + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                    + " require_unity_compute=" + RequireUnityCompute
                    + " require_strict_zero_copy=" + RequireStrictZeroCopy
                    + " presentation_path=" + snapshot.PresentationPath
                    + " presented_zero_copy="
                    + HasNativeVideoFrameFlag(
                        snapshot.Flags,
                        MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy)
                    + " presented_cpu_fallback="
                    + HasNativeVideoFrameFlag(
                        snapshot.Flags,
                        MediaNativeInteropCommon.NativeVideoFrameFlagCpuFallback)
                    + " source_zero_copy="
                    + HasNativeVideoFrameFlag(
                        snapshot.SourceFlags,
                        MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy)
                    + " strict_zero_copy_active=" + snapshot.StrictZeroCopyActive
                    + BackendSummary);
                return false;
            }

            Debug.Log(
                "[CodexNativeValidation] result=passed advance="
                + playbackAdvance.ToString("F3")
                + " surface=" + snapshot.SurfaceKind
                + " pixel_format=" + snapshot.PixelFormat
                + " source_surface=" + snapshot.SourceSurfaceKind
                + " source_pixel_format=" + snapshot.SourcePixelFormat
                + " prefer_unity_direct_shader=" + Player.PreferNativeVideoUnityDirectShader
                + " require_unity_direct_shader=" + RequireUnityDirectShader
                + " prefer_unity_compute=" + Player.PreferNativeVideoUnityCompute
                + " require_direct_binding=" + RequireDirectBinding
                + " require_unity_compute=" + RequireUnityCompute
                + " observed_frame_direct_bindable="
                + _observedDirectBindableFrameDuringWindow
                + " observed_native_texture_bound="
                + (_observedDirectBindingDuringWindow ? "true" : "false")
                + " observed_source_plane_textures="
                + _observedSourcePlaneTexturesDuringWindow
                + " observed_direct_shader_path="
                + _observedDirectShaderPathDuringWindow
                + " observed_compute_path="
                + _observedComputePathDuringWindow
                + " binding_state_source="
                + snapshot.NativeTextureBindingStateSource
                + " source_zero_copy="
                + HasNativeVideoFrameFlag(
                    snapshot.SourceFlags,
                    MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy)
                + " presented_zero_copy="
                + HasNativeVideoFrameFlag(
                    snapshot.Flags,
                    MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy)
                + " presented_cpu_fallback="
                + HasNativeVideoFrameFlag(
                    snapshot.Flags,
                    MediaNativeInteropCommon.NativeVideoFrameFlagCpuFallback)
                + " interop_zero_copy_supported="
                + snapshot.InteropZeroCopySupported
                + " source_zero_copy_supported="
                + snapshot.SourceSurfaceZeroCopySupported
                + " fallback_copy_path_supported="
                + snapshot.FallbackCopyPathSupported
                + " source_plane_textures_supported="
                + snapshot.SourcePlaneTexturesSupported
                + " presented_direct_bindable_supported="
                + snapshot.PresentedFrameDirectBindableSupported
                + " presented_strict_zero_copy_supported="
                + snapshot.PresentedFrameStrictZeroCopySupported
                + " flags=0x" + snapshot.Flags.ToString("X")
                + " source_flags=0x" + snapshot.SourceFlags.ToString("X")
                + " source_plane_texture_flags=0x" + snapshot.SourcePlaneTextureFlags.ToString("X")
                + " direct_shader_path_active=" + snapshot.DirectShaderPathActive
                + " compute_path_active=" + snapshot.ComputePathActive
                + " activation_decision=" + snapshot.ActivationDecision
                + " has_bound_native_video_plane_textures=" + snapshot.HasBoundNativeVideoPlaneTextures
                + " native_video_plane_texture_bind_count=" + snapshot.NativeVideoPlaneTextureBindCount
                + " native_video_direct_shader_bind_count="
                + snapshot.NativeVideoDirectShaderBindCount
                + " presentation_path=" + snapshot.PresentationPath
                + " zero_cpu_copy_active=" + snapshot.ZeroCpuCopyActive
                + " source_plane_zero_copy_active="
                + snapshot.SourcePlaneTexturesZeroCopyActive
                + " strict_zero_copy_active=" + snapshot.StrictZeroCopyActive
                + " require_strict_zero_copy=" + RequireStrictZeroCopy
                + " strict_zero_copy="
                + EvaluateStrictZeroCopyObserved(snapshot)
                + " source_equals_presented_handle="
                + (snapshot.HasNativeSourceFrame
                    && snapshot.HasNativeFrame
                    && snapshot.SourceNativeHandle == snapshot.NativeHandle)
                + BackendSummary);
            return true;
        }

        private ValidationSnapshot CaptureSnapshot()
        {
            var playbackTime = SafeReadPlaybackTime();
            var hasTexture = Player.TargetMaterial != null
                && (Player.TargetMaterial.mainTexture != null
                    || Player.IsNativeVideoDirectShaderPathActive);
            MediaPlayer.NativeVideoInteropInfo interopInfo;
            var hasInteropInfo = Player.TryGetNativeVideoInteropInfo(out interopInfo);
            MediaPlayer.NativeVideoFrameInfo frameInfo;
            var hasNativeFrame = Player.TryGetLastNativeVideoFrameInfo(out frameInfo);
            var frameDirectBindable = hasNativeFrame && CanDirectlyBindNativeFrame(frameInfo);
            MediaPlayer.NativeVideoFrameInfo sourceFrameInfo;
            var hasNativeSourceFrame = Player.TryAcquireNativeVideoSourceFrameInfo(out sourceFrameInfo);
            if (hasNativeSourceFrame)
            {
                Player.ReleaseNativeVideoFrameInfo(sourceFrameInfo.FrameIndex);
            }
            MediaPlayer.NativeVideoPlaneTexturesInfo sourcePlaneTexturesInfo;
            var hasSourcePlaneTextures = Player.TryAcquireNativeVideoSourcePlaneTexturesInfo(
                out sourcePlaneTexturesInfo);
            bool nativeTextureBound;
            string nativeTextureBindingStateSource;
            var hasNativeTextureBindingState = TryReadNativeTextureBindingState(
                out nativeTextureBound,
                out nativeTextureBindingStateSource);

            var sourceSurfaceZeroCopyActive = hasNativeSourceFrame
                && HasNativeVideoFrameFlag(
                    sourceFrameInfo.Flags,
                    MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            var sourcePlaneTexturesZeroCopyActive = hasSourcePlaneTextures
                && HasNativeVideoFrameFlag(
                    sourcePlaneTexturesInfo.Flags,
                    MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);

            var snapshot = new ValidationSnapshot
            {
                PlaybackTime = playbackTime,
                HasTexture = hasTexture,
                HasNativeFrame = hasNativeFrame,
                FrameDirectBindable = frameDirectBindable,
                HasNativeTextureBindingState = hasNativeTextureBindingState,
                NativeTextureBound = hasNativeTextureBindingState && nativeTextureBound,
                NativeTextureBindingStateSource = nativeTextureBindingStateSource,
                InteropZeroCopySupported = hasInteropInfo && interopInfo.ZeroCopySupported,
                SourceSurfaceZeroCopySupported =
                    hasInteropInfo && interopInfo.SourceSurfaceZeroCopySupported,
                FallbackCopyPathSupported =
                    hasInteropInfo && interopInfo.FallbackCopyPathSupported,
                SourcePlaneTexturesSupported =
                    hasInteropInfo && interopInfo.SourcePlaneTexturesSupported,
                PresentedFrameDirectBindableSupported =
                    hasInteropInfo && interopInfo.PresentedFrameDirectBindable,
                PresentedFrameStrictZeroCopySupported =
                    hasInteropInfo && interopInfo.PresentedFrameStrictZeroCopySupported,
                HasSourcePlaneTextures = hasSourcePlaneTextures,
                DirectShaderPathActive = Player.IsNativeVideoDirectShaderPathActive,
                ComputePathActive = Player.IsNativeVideoComputePathActive,
                HasBoundNativeVideoPlaneTextures = Player.HasBoundNativeVideoPlaneTextures,
                NativeVideoPlaneTextureBindCount = Player.NativeVideoPlaneTextureBindCount,
                NativeVideoDirectShaderBindCount = Player.NativeVideoDirectShaderBindCount,
                ActivationDecision = Player.NativeVideoActivationDecision.ToString(),
                PresentationPath = Player.NativeVideoPresentationPath.ToString(),
                ZeroCpuCopyActive = Player.IsNativeVideoZeroCpuCopyActive || sourceSurfaceZeroCopyActive,
                SourcePlaneTexturesZeroCopyActive =
                    Player.IsNativeVideoSourcePlaneTexturesZeroCopyActive
                    || sourcePlaneTexturesZeroCopyActive,
                StrictZeroCopyActive = false,
                FrameIndex = hasNativeFrame ? frameInfo.FrameIndex : -1,
                NativeHandle = hasNativeFrame ? frameInfo.NativeHandle : IntPtr.Zero,
                SurfaceKind = hasNativeFrame ? frameInfo.SurfaceKind : NativeVideoSurfaceKind.Unknown,
                PixelFormat = hasNativeFrame ? frameInfo.PixelFormat : NativeVideoPixelFormatKind.Unknown,
                Flags = hasNativeFrame ? frameInfo.Flags : 0u,
                HasNativeSourceFrame = hasNativeSourceFrame,
                SourceNativeHandle = hasNativeSourceFrame ? sourceFrameInfo.NativeHandle : IntPtr.Zero,
                SourceSurfaceKind = hasNativeSourceFrame ? sourceFrameInfo.SurfaceKind : NativeVideoSurfaceKind.Unknown,
                SourcePixelFormat = hasNativeSourceFrame ? sourceFrameInfo.PixelFormat : NativeVideoPixelFormatKind.Unknown,
                SourceFlags = hasNativeSourceFrame ? sourceFrameInfo.Flags : 0u,
                SourcePlaneTextureFlags = hasSourcePlaneTextures ? sourcePlaneTexturesInfo.Flags : 0u,
            };
            snapshot.StrictZeroCopyActive = EvaluateStrictZeroCopyObserved(snapshot);
            return snapshot;
        }

        private double SafeReadPlaybackTime()
        {
            try
            {
                return Player != null ? Player.Time() : -1.0;
            }
            catch
            {
                return -1.0;
            }
        }

        private static string TryReadStringArgument(string prefix)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(prefix.Length);
                }
            }

            return string.Empty;
        }

        private static float TryReadFloatArgument(string prefix, float fallback)
        {
            float parsedValue;
            return float.TryParse(
                    TryReadStringArgument(prefix),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out parsedValue)
                ? parsedValue
                : fallback;
        }

        private static int TryReadIntArgument(string prefix, int fallback)
        {
            int parsedValue;
            return int.TryParse(
                    TryReadStringArgument(prefix),
                    out parsedValue)
                ? parsedValue
                : fallback;
        }

        private static bool TryReadBoolArgument(
            string prefix,
            bool fallback,
            out bool hasExplicitValue)
        {
            var rawValue = TryReadStringArgument(prefix);
            hasExplicitValue = !string.IsNullOrEmpty(rawValue);
            bool parsedValue;
            return hasExplicitValue && TryParseBoolean(rawValue, out parsedValue)
                ? parsedValue
                : fallback;
        }

        private static bool TryParseBoolean(string rawValue, out bool parsedValue)
        {
            parsedValue = false;
            if (string.IsNullOrEmpty(rawValue))
            {
                return false;
            }

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    parsedValue = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    parsedValue = false;
                    return true;
                default:
                    return false;
            }
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
                    Debug.LogWarning("[CodexNativeValidation] ignore unknown backend=" + rawValue);
                    return false;
            }
        }

        private bool TryReadNativeTextureBindingState(
            out bool nativeTextureBound,
            out string source)
        {
            nativeTextureBound = false;
            source = "unavailable";
            if (Player == null)
            {
                source = "player-missing";
                return false;
            }

            if (TryReadNativeTextureBindingStateFromProperty(
                "IsNativeTextureBound",
                out nativeTextureBound))
            {
                source = "MediaPlayer.IsNativeTextureBound";
                return true;
            }

            if (TryReadNativeTextureBindingStateFromProperty(
                "HasBoundNativeVideoTexture",
                out nativeTextureBound))
            {
                source = "MediaPlayer.HasBoundNativeVideoTexture";
                return true;
            }

            if (TryReadNativeTextureBindingStateFromProperty(
                "HasDirectlyBoundNativeVideoTexture",
                out nativeTextureBound))
            {
                source = "MediaPlayer.HasDirectlyBoundNativeVideoTexture";
                return true;
            }

            if (TryReadNativeTextureBindingStateFromProperty(
                "HasBoundNativeTexture",
                out nativeTextureBound))
            {
                source = "MediaPlayer.HasBoundNativeTexture";
                return true;
            }

            if (TryReadNativeTextureBindingStateFromProperty(
                "HasDirectlyBoundNativeTexture",
                out nativeTextureBound))
            {
                source = "MediaPlayer.HasDirectlyBoundNativeTexture";
                return true;
            }

            if (TryReadNativeTextureBindingStateFromMethod(
                "TryGetNativeTextureBindingState",
                out nativeTextureBound))
            {
                source = "MediaPlayer.TryGetNativeTextureBindingState";
                return true;
            }

            if (TryReadNativeTextureBindingStateFromMethod(
                "TryGetNativeVideoTextureBindingState",
                out nativeTextureBound))
            {
                source = "MediaPlayer.TryGetNativeVideoTextureBindingState";
                return true;
            }

            return false;
        }

        private bool TryReadNativeTextureBindingStateFromProperty(
            string propertyName,
            out bool nativeTextureBound)
        {
            nativeTextureBound = false;
            var property = Player.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            if (property == null
                || property.PropertyType != typeof(bool)
                || !property.CanRead)
            {
                return false;
            }

            object rawValue;
            try
            {
                rawValue = property.GetValue(Player, null);
            }
            catch
            {
                return false;
            }

            if (!(rawValue is bool))
            {
                return false;
            }

            nativeTextureBound = (bool)rawValue;
            return true;
        }

        private bool TryReadNativeTextureBindingStateFromMethod(
            string methodName,
            out bool nativeTextureBound)
        {
            nativeTextureBound = false;
            var method = Player.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(bool).MakeByRefType() },
                null);
            if (method == null || method.ReturnType != typeof(bool))
            {
                return false;
            }

            object[] arguments = { false };
            object invocationResult;
            try
            {
                invocationResult = method.Invoke(Player, arguments);
            }
            catch
            {
                return false;
            }

            if (!(invocationResult is bool)
                || !(bool)invocationResult
                || !(arguments[0] is bool))
            {
                return false;
            }

            nativeTextureBound = (bool)arguments[0];
            return true;
        }

        private static bool CanDirectlyBindNativeFrame(MediaPlayer.NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return frameInfo.SurfaceKind == NativeVideoSurfaceKind.D3D11Texture2D
                && frameInfo.PixelFormat == NativeVideoPixelFormatKind.Rgba32
                && frameInfo.NativeHandle != IntPtr.Zero;
#else
            return false;
#endif
        }

        private static bool HasNativeVideoFrameFlag(uint flags, uint mask)
        {
            return (flags & mask) != 0u;
        }

        private static string FormatBindingState(ValidationSnapshot snapshot)
        {
            if (!snapshot.HasNativeTextureBindingState)
            {
                return "unknown";
            }

            return snapshot.NativeTextureBound ? "true" : "false";
        }

        private IEnumerator QuitAfterDelay(float delaySeconds, int exitCode)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            Debug.Log("[CodexNativeValidation] complete exitCode=" + exitCode);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }

        private struct ValidationSnapshot
        {
            public double PlaybackTime;
            public bool HasTexture;
            public bool HasNativeFrame;
            public bool FrameDirectBindable;
            public bool HasNativeTextureBindingState;
            public bool NativeTextureBound;
            public string NativeTextureBindingStateSource;
            public bool InteropZeroCopySupported;
            public bool SourceSurfaceZeroCopySupported;
            public bool FallbackCopyPathSupported;
            public bool SourcePlaneTexturesSupported;
            public bool PresentedFrameDirectBindableSupported;
            public bool PresentedFrameStrictZeroCopySupported;
            public bool HasSourcePlaneTextures;
            public bool DirectShaderPathActive;
            public bool ComputePathActive;
            public bool HasBoundNativeVideoPlaneTextures;
            public long NativeVideoPlaneTextureBindCount;
            public long NativeVideoDirectShaderBindCount;
            public string ActivationDecision;
            public string PresentationPath;
            public bool ZeroCpuCopyActive;
            public bool SourcePlaneTexturesZeroCopyActive;
            public bool StrictZeroCopyActive;
            public long FrameIndex;
            public IntPtr NativeHandle;
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormatKind PixelFormat;
            public uint Flags;
            public bool HasNativeSourceFrame;
            public IntPtr SourceNativeHandle;
            public NativeVideoSurfaceKind SourceSurfaceKind;
            public NativeVideoPixelFormatKind SourcePixelFormat;
            public uint SourceFlags;
            public uint SourcePlaneTextureFlags;
        }
    }
}
