using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KingdomWatch.Game.Editor
{
    // The Play Store build, headless, run by .github/workflows/android-release.yml:
    // Unity -batchmode -projectPath Game -buildTarget Android -executeMethod
    // KingdomWatch.Game.Editor.ReleaseBuild.Android -releaseVersion 1.2.3
    // -releaseVersionCode 261005001 -keystorePath ... It writes a signed Android
    // App Bundle to Game/Builds/Android (git-ignored) and exits 0 once built, 1
    // otherwise.
    //
    // It states the release settings itself rather than reading them off the
    // Android build profile, which is the development profile (Development
    // Build and Autoconnect Profiler on, see docs/unity.md). A release must not
    // depend on which profile was last active in whoever's Editor committed.
    //
    // The version and signing arrive as arguments because the version code has
    // to be unique per upload and the keystore is a secret; nothing here is
    // committed to ProjectSettings.asset.
    public static class ReleaseBuild
    {
        public static void Android()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                PlayerSettings.bundleVersion = Required(args, "-releaseVersion");
                PlayerSettings.Android.bundleVersionCode = int.Parse(Required(args, "-releaseVersionCode"));

                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Required(args, "-keystorePath");
                PlayerSettings.Android.keystorePass = Required(args, "-keystorePass");
                PlayerSettings.Android.keyaliasName = Required(args, "-keyaliasName");
                PlayerSettings.Android.keyaliasPass = Required(args, "-keyaliasPass");

                var targetSdk = Optional(args, "-targetSdk");
                if (targetSdk != null)
                    PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)int.Parse(targetSdk);

                EditorUserBuildSettings.buildAppBundle = true;
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;

                var project = Directory.GetParent(Application.dataPath).FullName;
                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = Path.Combine(project, "Builds", "Android", "KingdomWatch.aab"),
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(options);
                Debug.Log("ReleaseBuild: " + report.summary.result + ", " + report.summary.totalErrors + " errors, " + options.locationPathName);
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                // Any failure, a missing argument included, must exit non-zero:
                // an unhandled exception in -executeMethod can leave the process
                // exiting 0 with no bundle, which the workflow would report as a
                // build and then fail to find.
                Debug.LogError("ReleaseBuild: " + e.Message);
                EditorApplication.Exit(1);
            }
        }

        static string Optional(string[] args, string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static string Required(string[] args, string name) =>
            Optional(args, name) ?? throw new ArgumentException("missing argument " + name);
    }
}
