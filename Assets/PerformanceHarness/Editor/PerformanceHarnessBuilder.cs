// Editor-time build tool for the Tier 2 release-Player performance harness
// (Assets/PerformanceHarness/ReleaseBenchmarkHarness.cs). This assembly
// (mvcExpress.PerformanceHarness.Editor, see the sibling .asmdef) only compiles for the Editor
// platform, so it is excluded from every Player build by definition - it is a build-time tool,
// not something that should ever ship inside the Player it builds.
using mvcExpress.PerformanceHarness;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace mvcExpress.Editor
{
    /// <summary>
    /// Builds a real (non-development) Standalone Player containing only the release benchmark
    /// scene, for Tier 2 performance measurement. Invoke via:
    /// <c>-executeMethod mvcExpress.Editor.PerformanceHarnessBuilder.BuildReleaseBenchmarkPlayer</c>.
    /// </summary>
    public static class PerformanceHarnessBuilder
    {
        private const string SceneDirectory = "Assets/PerformanceHarness";
        private const string ScenePath = SceneDirectory + "/ReleaseBenchmarkScene.unity";
        private const string BuildOutputPath = "Builds/PerformanceHarness/ReleaseBenchmarkHarness.exe";

        /// <summary>
        /// Ensures the benchmark scene exists (creating it programmatically if missing, since a
        /// GameObject+component scene is far more reliable to generate via the Editor API than to
        /// hand-author as raw YAML), then builds a StandaloneWindows64 Player with
        /// <see cref="BuildOptions.None"/> (explicitly NOT <see cref="BuildOptions.Development"/> -
        /// the whole point of Tier 2 is real release-mode numbers) containing only that scene.
        /// </summary>
        [MenuItem("mvcExpress/Performance Harness/Build Release Benchmark Player")]
        public static void BuildReleaseBenchmarkPlayer()
        {
            EnsureBenchmarkScene();

            string outputDirectory = Path.GetDirectoryName(BuildOutputPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildOutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                // Explicitly None, not Development: Tier 1 could only measure inside the Editor's
                // batchmode PlayMode test runner, which always carries UNITY_EDITOR/dev-build-like
                // stripping semantics. This build option is what actually gets us a true release
                // Player, so real GC.GetAllocatedBytesForCurrentThread() numbers can be trusted.
                options = BuildOptions.None
            };

            Debug.Log($"[PerformanceHarnessBuilder] Building release benchmark Player to '{BuildOutputPath}' " +
                      $"(target={buildPlayerOptions.target}, options={buildPlayerOptions.options}).");

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            Debug.Log($"[PerformanceHarnessBuilder] Build finished: result={report.summary.result}, " +
                      $"totalErrors={report.summary.totalErrors}, totalWarnings={report.summary.totalWarnings}, " +
                      $"outputPath={report.summary.outputPath}, totalSize={report.summary.totalSize} bytes.");

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                // Fail loudly (non-zero exit via thrown exception) so -batchmode callers get a
                // non-zero process exit code on build failure instead of silently succeeding.
                throw new BuildFailedException(
                    $"Release benchmark Player build did not succeed (result={report.summary.result}).");
            }
        }

        // Creates Assets/PerformanceHarness/ReleaseBenchmarkScene.unity containing a single
        // GameObject with the ReleaseBenchmarkHarness component, if it doesn't already exist.
        // Safe to call repeatedly - a no-op once the scene is present on disk.
        private static void EnsureBenchmarkScene()
        {
            if (!Directory.Exists(SceneDirectory))
            {
                Directory.CreateDirectory(SceneDirectory);
            }

            if (File.Exists(ScenePath))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var harnessGo = new GameObject("ReleaseBenchmarkHarness");
            harnessGo.AddComponent<ReleaseBenchmarkHarness>();
            SceneManager.MoveGameObjectToScene(harnessGo, scene);

            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[PerformanceHarnessBuilder] Created benchmark scene at '{ScenePath}'.");
        }

        private sealed class BuildFailedException : System.Exception
        {
            public BuildFailedException(string message) : base(message) { }
        }
    }
}
