#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildIcarusExcitometerAndroid
{
    private const string OutputDirectory = "Builds";
    private const string OutputApk = "Builds/IcarusExcitometer.apk";

    [MenuItem("Build/Icarus Excitometer Android APK")]
    public static void BuildApk()
    {
        PlayerSettings.companyName = "Viscereality";
        PlayerSettings.productName = "Icarus Excitometer";
        PlayerSettings.bundleIdentifier = "com.viscereality.icarusexcitometer";

        if (!Directory.Exists(OutputDirectory))
            Directory.CreateDirectory(OutputDirectory);

        string[] scenes = new string[]
        {
            "Assets/_Scenes/Main.unity"
        };

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTarget.Android);
        BuildPipeline.BuildPlayer(scenes, OutputApk, BuildTarget.Android, BuildOptions.None);
    }

    public static void BuildFromCommandLine()
    {
        BuildApk();
    }
}
#endif
