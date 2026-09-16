using UnityEditor;

/// <summary>
/// 一次性构建器（不入 Assets/、不入库）：创建 UI 界面 prefab 的目标目录。
/// 背景：Pipeline 的 move_asset 要求目标父目录已存在（见执行计划风险表第 1 条）。
/// 用法：unity command run_script --file AgentScripts/MakeScreensDir.cs --entry MakeScreensDir.Run
/// </summary>
public static class MakeScreensDir
{
    private const string Parent = "Assets/LiteGame/UI";
    private const string Leaf = "Screens";

    public static int Run()
    {
        string path = Parent + "/" + Leaf;
        if (AssetDatabase.IsValidFolder(path))
        {
            UnityEngine.Debug.Log($"[MakeScreensDir] 已存在：{path}");
            return 0;
        }

        if (!AssetDatabase.IsValidFolder(Parent))
        {
            UnityEngine.Debug.LogError($"[MakeScreensDir] 父目录不存在：{Parent}");
            return 2;
        }

        string guid = AssetDatabase.CreateFolder(Parent, Leaf);
        if (string.IsNullOrEmpty(guid))
        {
            UnityEngine.Debug.LogError($"[MakeScreensDir] 创建失败：{path}");
            return 3;
        }

        AssetDatabase.Refresh();
        UnityEngine.Debug.Log($"[MakeScreensDir] 已创建：{path}（guid={guid}）");
        return 0;
    }
}
