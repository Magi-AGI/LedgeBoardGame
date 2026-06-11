using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magi.LedgeBoardGame.Editor
{
    /// One-shot WebGL-with-WebGPU build helper. Collects scenes from the
    /// current EditorBuildSettings list, forces the WebGL graphics API to
    /// WebGPU (Unity 6 pipeline), and writes the player to Builds/WebGPU
    /// under the project root. Requires the WebGL build support module —
    /// if it's missing the method throws instead of silently falling back
    /// to a plain WebGL (OpenGLES3) build.
    ///
    /// Usage:
    ///   • Menu: Magi → Build → WebGPU (browser)
    ///   • CLI:  Unity.exe -quit -batchmode -projectPath ... -executeMethod
    ///           Magi.LedgeBoardGame.Editor.WebGpuBuilder.BuildWebGpu
    ///
    /// The output folder is deleted before each build so stale assets don't
    /// leak in; deploy the full Builds/WebGPU directory to the web host.
    public static class WebGpuBuilder
    {
        private const string OutputDirName = "Builds/WebGPU";

        [MenuItem("Magi/Build/WebGPU (browser)")]
        public static void BuildWebGpuMenu()
        {
            try
            {
                var report = BuildWebGpu();
                EditorUtility.DisplayDialog(
                    "WebGPU build",
                    $"Wrote {report.summary.outputPath}\n\n" +
                    $"Result: {report.summary.result}\n" +
                    $"Duration: {report.summary.totalTime}\n" +
                    $"Size: {report.summary.totalSize / (1024 * 1024)} MiB",
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("WebGPU build failed", ex.Message, "OK");
                throw;
            }
        }

        public static UnityEditor.Build.Reporting.BuildReport BuildWebGpu()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                throw new InvalidOperationException(
                    "WebGL build support is not installed in this Unity editor. " +
                    "Install the 'WebGL Build Support' module via Unity Hub → " +
                    "Installs → Add Modules, then re-run this command.");
            }

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "No enabled scenes in Build Settings. Add at least the gameplay scene before building.");

            // Unity 6 populates the GraphicsDeviceType enum with WebGPU
            // even on editors missing the WebGL module, so setting the
            // API only falls through to the runtime check above. Setting
            // it unconditionally keeps the helper idempotent — calling
            // BuildWebGpu twice doesn't leave us in OpenGLES3.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.WebGPU });

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var output = Path.Combine(projectRoot, OutputDirName);
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.CompressWithLz4,
            };

            UnityEngine.Debug.Log($"[WebGpuBuilder] Building {scenes.Length} scene(s) to {output} with WebGPU graphics API…");
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"WebGPU build finished with result={report.summary.result}. " +
                    "Inspect the Unity console for the underlying errors.");
            }
            UnityEngine.Debug.Log($"[WebGpuBuilder] Build succeeded. Output: {output}");
            return report;
        }
    }
}
