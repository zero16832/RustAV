using System;
using System.Collections.Generic;
using System.IO;
using Process = System.Diagnostics.Process;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityAV.Editor
{
    /// <summary>
    /// 为 batchmode 提供最小场景级验证入口。
    /// </summary>
    public static class CodexValidationBuild
    {
        private const string ScenePath = "Assets/UnityAV/Validation/CodexPullValidation.generated.unity";
        private const string MaterialPath = "Assets/UnityAV/Materials/VideoMaterial.mat";
        private const string BuildPath = "Build/CodexPullValidation/CodexPullValidation.exe";
        private const string SampleUri = "SampleVideo_1280x720_10mb.mp4";
        private const int DefaultVideoWidth = 1280;
        private const int DefaultVideoHeight = 720;
        private const int BuildOutputDeleteRetryCount = 10;
        private const int BuildOutputDeleteRetryDelayMs = 500;
        private const int BuildOutputLockContextLimit = 5;

        public static void CreatePullValidationScene()
        {
            DeleteGeneratedValidationScene();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = CreateCamera();
            var surface = CreateVideoSurface();
            var player = CreateValidationPlayer();
            CreateDriver(player, surface.transform, camera);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/UnityAV/Validation");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[CodexValidationBuild] scene_created=" + ScenePath);
        }

        public static void BuildWindowsValidationPlayer()
        {
            WindowsNativeRuntimePackager.ConfigureProjectRuntimeImportSettings();
            WindowsNativeRuntimePackager.EnsureProjectRuntimeAvailable();
            CreatePullValidationScene();

            PrepareBuildOutput(BuildPath);
            var previousFullScreenMode = PlayerSettings.fullScreenMode;
            var previousDefaultScreenWidth = PlayerSettings.defaultScreenWidth;
            var previousDefaultScreenHeight = PlayerSettings.defaultScreenHeight;
            var previousResizableWindow = PlayerSettings.resizableWindow;
            var previousDefaultIsNativeResolution = PlayerSettings.defaultIsNativeResolution;

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = DefaultVideoWidth;
            PlayerSettings.defaultScreenHeight = DefaultVideoHeight;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultIsNativeResolution = false;

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = BuildPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                PlayerSettings.fullScreenMode = previousFullScreenMode;
                PlayerSettings.defaultScreenWidth = previousDefaultScreenWidth;
                PlayerSettings.defaultScreenHeight = previousDefaultScreenHeight;
                PlayerSettings.resizableWindow = previousResizableWindow;
                PlayerSettings.defaultIsNativeResolution = previousDefaultIsNativeResolution;
                DeleteGeneratedValidationScene();
            }

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new System.Exception(
                    "Windows 验证包构建失败: " + report.summary.result);
            }

            WindowsNativeRuntimePackager.PackageGstreamerRuntimeOrThrow(BuildPath);

            Debug.Log("[CodexValidationBuild] build_succeeded=" + BuildPath);
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.transform.rotation = Quaternion.identity;

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;

            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static GameObject CreateVideoSurface()
        {
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Video Surface";
            surface.transform.position = Vector3.zero;
            surface.transform.localScale = new Vector3(
                (float)DefaultVideoWidth / DefaultVideoHeight,
                1f,
                1f);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
            {
                var renderer = surface.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }

            return surface;
        }

        private static MediaPlayerPull CreateValidationPlayer()
        {
            var playerObject = new GameObject("Validation Player");
            var player = playerObject.AddComponent<MediaPlayerPull>();
            player.Uri = SampleUri;
            player.Loop = false;
            player.AutoPlay = true;
            player.Width = DefaultVideoWidth;
            player.Height = DefaultVideoHeight;
            player.EnableAudio = true;
            player.AutoStartAudio = true;
            player.TargetMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            return player;
        }

        private static void CreateDriver(MediaPlayerPull player, Transform surface, Camera camera)
        {
            var driver = player.gameObject.AddComponent<CodexValidationDriver>();
            driver.Player = player;
            driver.ValidationSeconds = 6f;
            driver.LogIntervalSeconds = 1f;
            driver.VideoSurface = surface;
            driver.ValidationCamera = camera;
        }

        private static void DeleteGeneratedValidationScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.DeleteAsset(ScenePath))
            {
                AssetDatabase.Refresh();
            }
        }

        private static void PrepareBuildOutput(string buildPath)
        {
            StopRunningValidationPlayer(buildPath);

            var buildDirectory = Path.GetDirectoryName(buildPath) ?? "Build/CodexPullValidation";
            DeleteDirectoryWithRetries(buildDirectory);

            Directory.CreateDirectory(buildDirectory);
        }

        private static void StopRunningValidationPlayer(string buildPath)
        {
            var exePath = Path.GetFullPath(buildPath);
            var processName = Path.GetFileNameWithoutExtension(exePath);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var processPath = process.MainModule?.FileName ?? string.Empty;
                    if (!string.Equals(processPath, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                catch
                {
                    // 无法读取路径时保守处理，直接继续尝试退出。
                }

                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                }
            }
        }

        private static void DeleteDirectoryWithRetries(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            Exception lastException = null;
            for (var attempt = 1; attempt <= BuildOutputDeleteRetryCount; attempt++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastException = ex;
                }

                if (!Directory.Exists(path))
                {
                    return;
                }

                if (attempt < BuildOutputDeleteRetryCount)
                {
                    Debug.LogWarning(
                        $"[CodexValidationBuild] build_output_delete_retry path={path} attempt={attempt}/{BuildOutputDeleteRetryCount} reason={lastException?.GetType().Name}: {lastException?.Message}");
                    Thread.Sleep(BuildOutputDeleteRetryDelayMs);
                }
            }

            var failureMessage = BuildOutputDeleteFailureMessage(path, lastException);
            if (lastException is UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException(failureMessage, lastException);
            }

            throw new IOException(failureMessage, lastException);
        }

        private static string BuildOutputDeleteFailureMessage(string path, Exception lastException)
        {
            var builder = new StringBuilder();
            builder.Append("删除构建输出目录失败：");
            builder.Append(path);
            builder.Append("。已重试 ");
            builder.Append(BuildOutputDeleteRetryCount);
            builder.Append(" 次，每次等待 ");
            builder.Append(BuildOutputDeleteRetryDelayMs);
            builder.Append("ms。");

            if (lastException != null)
            {
                builder.Append("最后一次异常=");
                builder.Append(lastException.GetType().Name);
                builder.Append(": ");
                builder.Append(lastException.Message);
                builder.Append("。");
            }

            builder.Append(BuildOutputLockContext(path));
            return builder.ToString();
        }

        private static string BuildOutputLockContext(string path)
        {
            var builder = new StringBuilder();
            builder.Append(" 可能的文件锁上下文：");

            if (!Directory.Exists(path))
            {
                builder.Append("重试结束后目录已不存在，可能是外部进程在最后一次重试后完成了释放。");
                return builder.ToString();
            }

            var blockedFiles = new List<string>();
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var blockedFile = TryDescribeBlockedFile(filePath);
                    if (string.IsNullOrEmpty(blockedFile))
                    {
                        continue;
                    }

                    blockedFiles.Add(blockedFile);
                    if (blockedFiles.Count >= BuildOutputLockContextLimit)
                    {
                        break;
                    }
                }
            }
            catch (IOException ex)
            {
                builder.Append("扫描目录时遇到 IOException: ");
                builder.Append(ex.Message);
                builder.Append("。");
                return builder.ToString();
            }
            catch (UnauthorizedAccessException ex)
            {
                builder.Append("扫描目录时遇到 UnauthorizedAccessException: ");
                builder.Append(ex.Message);
                builder.Append("。");
                return builder.ToString();
            }

            if (blockedFiles.Count == 0)
            {
                builder.Append("未定位到明确的被占用文件，可能是目录句柄、杀毒扫描或外部进程仍持有该目录。");
                return builder.ToString();
            }

            builder.Append("疑似仍被占用的文件=");
            builder.Append(string.Join(" | ", blockedFiles));
            return builder.ToString();
        }

        private static string TryDescribeBlockedFile(string filePath)
        {
            try
            {
                using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }

                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException ex)
            {
                return BuildBlockedFileDescription(filePath, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return BuildBlockedFileDescription(filePath, ex);
            }
        }

        private static string BuildBlockedFileDescription(string filePath, Exception ex)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                return $"{filePath} ({ex.GetType().Name}: {ex.Message}; size={fileInfo.Length}; lastWriteUtc={fileInfo.LastWriteTimeUtc:O})";
            }
            catch (IOException)
            {
                return $"{filePath} ({ex.GetType().Name}: {ex.Message}; metadata=unavailable)";
            }
            catch (UnauthorizedAccessException)
            {
                return $"{filePath} ({ex.GetType().Name}: {ex.Message}; metadata=unavailable)";
            }
        }
    }
}
