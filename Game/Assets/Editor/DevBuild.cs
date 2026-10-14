using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KingdomWatch.Game.Editor
{
    // A Windows development build for scripted runs (#132), headless:
    // Unity -batchmode -projectPath Game -buildTarget Win64 -executeMethod
    // KingdomWatch.Game.Editor.DevBuild.Windows. It lands in
    // Game/Builds/Windows (git-ignored) and exits 0 once built, 1 otherwise.
    // tools/Profile.ps1 runs it; -buildTarget saves a switch of the open
    // profile, which is Android.
    //
    // The scripted run is compiled in under UNITY_INCLUDE_INSTRUMENTATION,
    // which Unity 6.6 defines from the managed code variant rather than from
    // BuildOptions.Development (DEVELOPMENT_BUILD is deprecated), so the
    // variant is set here: Instrumented, the least that defines it.
    public static class DevBuild
    {
        public static void Windows()
        {
            PlayerSettings.SetManagedCodeVariant(NamedBuildTarget.Standalone, ManagedCodeVariant.Instrumented);
            var project = Directory.GetParent(Application.dataPath).FullName;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(project, "Builds", "Windows", "KingdomWatch.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("DevBuild: " + report.summary.result + ", " + report.summary.totalErrors + " errors, " + options.locationPathName);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        // The same for Android, as an APK for an emulator or a phone over USB:
        // -buildTarget Android -executeMethod KingdomWatch.Game.Editor.DevBuild.Android.
        // The variant is put back afterwards, since the release build reads the
        // same player settings. Unity builds ARM64 only, so an emulator needs
        // an image that translates ARM (Android 16's x86_64 ones do).
        public static void Android()
        {
            var variant = PlayerSettings.GetManagedCodeVariant(NamedBuildTarget.Android);
            PlayerSettings.SetManagedCodeVariant(NamedBuildTarget.Android, ManagedCodeVariant.Instrumented);
            EditorUserBuildSettings.buildAppBundle = false;
            var project = Directory.GetParent(Application.dataPath).FullName;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(project, "Builds", "Android", "KingdomWatch.apk"),
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            };
            var report = BuildPipeline.BuildPlayer(options);
            PlayerSettings.SetManagedCodeVariant(NamedBuildTarget.Android, variant);
            Debug.Log("DevBuild: " + report.summary.result + ", " + report.summary.totalErrors + " errors, " + options.locationPathName);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
