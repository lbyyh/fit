using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 命令行 / 菜单出包脚本。Windows 64 位 Standalone（Mono 后端）。
/// 用法（菜单）：Build > Build Windows 64
/// 用法（命令行）：Unity -batchmode -executeMethod BuildScript.BuildWin64 -quit
/// </summary>
public static class BuildScript
{
    private const string OutputDir = "Build/Win64";
    private const string ExeName = "fit.exe";

    [MenuItem("Build/Build Windows 64")]
    public static void BuildWin64Menu() => BuildWin64();

    public static void BuildWin64()
    {
        // 1) 优先取 Build Settings 中已启用的场景
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        // 2) 回退：搜 Assets 下所有 Scene 资产
        if (scenes.Length == 0)
        {
            var guids = AssetDatabase.FindAssets("t:Scene");
            scenes = guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
        }

        if (scenes.Length == 0)
        {
            Debug.LogError(
                "[Build] 没有可打包的场景。请先在 Build Settings 中添加场景，\n" +
                "或在 Assets 下创建至少一个 .unity 场景后再出包。");
            EditorApplication.Exit(3);
            return;
        }

        var outputPath = Path.Combine(OutputDir, ExeName);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Build] 成功：{Path.GetFullPath(outputPath)}");
        }
        else
        {
            Debug.LogError($"[Build] 失败：{report.summary.result}");
            EditorApplication.Exit(1);
        }
    }
}
