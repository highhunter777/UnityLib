using System.Collections.Generic;
using System.Text;
using UnityEngine;
using YooAsset.Editor;

/// <summary>
/// 一次性构建器（不入 Assets/、不入库）：编辑器态验证收集组覆盖——不依赖 Play / 启动场景
/// （当前工作区打开的是 SimSandbox，没有 GameEntry，跑不了运行时校验口）。
/// 走 YooAsset 自身的采集管线（BeginCollect, simulateBuild=true），逐组核对：
///   ① UI 目录下被采集的资源数（期望 26 = 25 控件 + 1 界面）
///   ② 每个资源带的 tag 是否含 ui
///   ③ 分包结果（PackSeparately 界面应各自成包；PackDirectory 控件应归入同目录包）
/// 用法：unity command run_script --file AgentScripts/CollectorVerify.cs --entry CollectorVerify.Run
/// </summary>
public static class CollectorVerify
{
    private const string PackageName = "DefaultPackage";
    private const string UiRoot = "Assets/LiteGame/UI/";
    private const int Expected = 26;

    public static int Run()
    {
        var setting = BundleCollectorSettingData.Setting;
        var result = setting.BeginCollect(PackageName, true, false);
        var assets = result.CollectAssets;

        int uiCount = 0;
        int uiTagged = 0;
        var byBundle = new SortedDictionary<string, int>();
        var missingTag = new List<string>();

        foreach (var a in assets)
        {
            string path = a.AssetInfo.AssetPath;
            if (!path.StartsWith(UiRoot)) continue;

            uiCount++;
            bool hasTag = a.AssetTags != null && a.AssetTags.Contains("ui");
            if (hasTag) uiTagged++;
            else missingTag.Add(path);

            byBundle.TryGetValue(a.BundleName, out int n);
            byBundle[a.BundleName] = n + 1;
        }

        var sb = new StringBuilder();
        sb.Append($"[CollectorVerify] 采集资源总数 {assets.Count}；UI 目录 {uiCount}（期望 {Expected}），带 ui tag {uiTagged}\n");
        sb.Append("[CollectorVerify] UI 分包：\n");
        foreach (var kv in byBundle) sb.Append($"    {kv.Key} ← {kv.Value} 件\n");
        foreach (var p in missingTag) sb.Append($"    [缺 ui tag] {p}\n");

        bool pass = uiCount == Expected && uiTagged == Expected;
        sb.Append($"[CollectorVerify] → {(pass ? "PASS" : "FAIL")}");
        if (pass) Debug.Log(sb.ToString());
        else Debug.LogError(sb.ToString());

        return pass ? 0 : 3;
    }

    /// <summary>
    /// 运行时校验（Play 模式；等价于菜单 `LiteGame/UI/校验收集组`，供无法点击菜单时走 CLI）：
    /// 经 <c>AssetService</c> 逐件加载两个收集目录下的全部 prefab，核对 tag 清单条目数。
    /// 用法：unity command run_script --file AgentScripts/CollectorVerify.cs --entry CollectorVerify.RunRuntime
    /// </summary>
    public static async System.Threading.Tasks.Task<int> RunRuntime()
    {
        if (!UnityEngine.Application.isPlaying)
        {
            Debug.LogError("[CollectorVerify] RunRuntime 需 Play 模式（AssetService 需已初始化）");
            return 2;
        }

        if (!LiteGame.AssetService.Initialized)
        {
            Debug.LogError("[CollectorVerify] AssetService 未初始化");
            return 4;
        }

        var guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/LiteGame/UI/Widgets", "Assets/LiteGame/UI/Screens" });
        var paths = new List<string>(guids.Length);
        foreach (var g in guids) paths.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
        paths.Sort(string.CompareOrdinal);

        int loaded = 0;
        foreach (var p in paths)
        {
            try
            {
                var go = await LiteGame.AssetService.LoadAssetAsync<UnityEngine.GameObject>(p);
                if (go == null) Debug.LogError($"[CollectorVerify] 加载返回 null：{p}");
                else loaded++;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CollectorVerify] 加载失败 {p}：{ex.GetType().Name}:{ex.Message}");
            }
        }

        int tagCount = -1;
        if (YooAsset.YooAssets.TryGetPackage(LiteGame.AssetService.DefaultPackageName, out var pkg))
            tagCount = pkg.GetAssetInfos("ui")?.Length ?? 0;

        bool pass = loaded == paths.Count && tagCount == paths.Count;
        string line = $"[CollectorVerify] 运行时：可加载 {loaded}/{paths.Count}，tag=ui 清单 {tagCount} 条 → {(pass ? "PASS" : "FAIL")}";
        if (pass) Debug.Log(line); else Debug.LogError(line);
        return pass ? 0 : 5;
    }
}
