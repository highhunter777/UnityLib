using UnityEditor;

/// <summary>
/// 一次性构建器（不入 Assets/、不入库）：删除搬迁后留下的空目录 Assets/UI（含其 .meta）。
/// 用法：unity command run_script --file AgentScripts/DeleteEmptyUIDir.cs --entry DeleteEmptyUIDir.Run
/// </summary>
public static class DeleteEmptyUIDir
{
    private const string Target = "Assets/UI";

    public static int Run()
    {
        if (!AssetDatabase.IsValidFolder(Target))
        {
            UnityEngine.Debug.Log($"[DeleteEmptyUIDir] 已不存在：{Target}");
            return 0;
        }

        bool ok = AssetDatabase.DeleteAsset(Target);
        if (!ok)
        {
            UnityEngine.Debug.LogError($"[DeleteEmptyUIDir] 删除失败：{Target}（可能非空）");
            return 3;
        }

        AssetDatabase.Refresh();
        UnityEngine.Debug.Log($"[DeleteEmptyUIDir] 已删除：{Target}");
        return 0;
    }
}
