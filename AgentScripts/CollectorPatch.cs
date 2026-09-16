using System.Collections.Generic;
using System.Text;
using UnityEngine;
using YooAsset.Editor;

/// <summary>
/// 一次性构建器（不入 Assets/、不入库）：把 UI prefab 收进 YooAsset 收集组。
/// 背景（《UI性能优化规划》缺口 1/3）：界面 prefab 已从 Assets/UI 迁到 Assets/LiteGame/UI/Screens，
/// 控件库 25 件在 Assets/LiteGame/UI/Widgets 但从未被任何收集组覆盖（真机加载不到）。
/// 约束：同组内两个 CollectPath 不得互相包含（否则同一 prefab 被双收）——故两条路径取兄弟目录。
///
/// 用法：
///   unity command run_script --file AgentScripts/CollectorPatch.cs --entry CollectorPatch.Inspect
///   unity command run_script --file AgentScripts/CollectorPatch.cs --entry CollectorPatch.Apply
/// </summary>
public static class CollectorPatch
{
    private const string PackageName = "DefaultPackage";
    private const string UiGroupName = "LiteGameUI";
    private const string WidgetGroupName = "LiteGameWidgets";
    private const string UiCollectPath = "Assets/LiteGame/UI/Screens";
    private const string WidgetCollectPath = "Assets/LiteGame/UI/Widgets";
    private const string Tag = "ui";

    /// <summary>只读：打印当前收集组现状。</summary>
    public static int Inspect()
    {
        var setting = BundleCollectorSettingData.Setting;
        var sb = new StringBuilder();
        sb.Append("[CollectorPatch] 现状：\n");
        foreach (var pkg in setting.Packages)
        {
            sb.Append($"  包 {pkg.PackageName}（EnableAddressable={pkg.EnableAddressable}，IgnoreRule={pkg.IgnoreRuleName}）\n");
            foreach (var g in pkg.Groups)
            {
                sb.Append($"    组 {g.GroupName}（ActiveRule={g.ActiveRuleName}，GroupTags=\"{g.AssetTags}\"）\n");
                foreach (var c in g.Collectors)
                    sb.Append($"      collector path=\"{c.CollectPath}\" address={c.AddressRuleName} pack={c.PackRuleName} " +
                              $"filter={c.FilterRuleName} tags=\"{c.AssetTags}\" type={c.CollectorType}\n");
            }
        }
        Debug.Log(sb.ToString());
        return 0;
    }

    /// <summary>写入：改 LiteGameUI 路径 + 新增 LiteGameWidgets 组，然后保存资产。</summary>
    public static int Apply()
    {
        var setting = BundleCollectorSettingData.Setting;
        var pkg = setting.GetPackage(PackageName);
        if (pkg == null)
        {
            Debug.LogError($"[CollectorPatch] 未找到包：{PackageName}");
            return 2;
        }

        // ① LiteGameUI 路径改指迁移后的界面目录
        BundleCollectorGroup uiGroup = null;
        foreach (var g in pkg.Groups)
            if (g.GroupName == UiGroupName) { uiGroup = g; break; }

        if (uiGroup == null)
        {
            Debug.LogError($"[CollectorPatch] 未找到组：{UiGroupName}");
            return 3;
        }

        if (uiGroup.Collectors.Count != 1)
        {
            Debug.LogError($"[CollectorPatch] {UiGroupName} collector 数异常：{uiGroup.Collectors.Count}（期望 1，人工核对后再跑）");
            return 4;
        }

        string before = uiGroup.Collectors[0].CollectPath;
        uiGroup.Collectors[0].CollectPath = UiCollectPath;
        Debug.Log($"[CollectorPatch] {UiGroupName} CollectPath：\"{before}\" -> \"{uiGroup.Collectors[0].CollectPath}\"");

        // ② 新增控件库组（幂等：已存在则只校验路径）
        BundleCollectorGroup widgetGroup = null;
        foreach (var g in pkg.Groups)
            if (g.GroupName == WidgetGroupName) { widgetGroup = g; break; }

        if (widgetGroup == null)
        {
            widgetGroup = new BundleCollectorGroup
            {
                GroupName = WidgetGroupName,
                GroupDesc = "UI 控件库模板（25 件，M4c）——PackDirectory 目录单包；tag ui",
                AssetTags = string.Empty,
                ActiveRuleName = nameof(EnableGroup),
            };
            widgetGroup.Collectors.Add(new BundleCollector
            {
                CollectPath = WidgetCollectPath,
                CollectorGUID = string.Empty,
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = nameof(PackDirectory),
                FilterRuleName = nameof(CollectAll),
                AssetTags = Tag,
                UserData = string.Empty,
            });
            pkg.Groups.Add(widgetGroup);
            Debug.Log($"[CollectorPatch] 已新增组 {WidgetGroupName}（path=\"{WidgetCollectPath}\"，pack=PackDirectory，tag={Tag}）");
        }
        else
        {
            if (widgetGroup.Collectors.Count == 0) widgetGroup.Collectors.Add(new BundleCollector());
            widgetGroup.Collectors[0].CollectPath = WidgetCollectPath;
            widgetGroup.Collectors[0].AssetTags = Tag;
            Debug.Log($"[CollectorPatch] 组 {WidgetGroupName} 已存在——路径/tag 已核对写入");
        }

        // ③ 规则名校验（无效规则名会抛）
        try
        {
            setting.CheckAllPackageConfigError();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CollectorPatch] 收集组配置校验失败（未保存）：{ex.Message}");
            return 5;
        }

        BundleCollectorSettingData.SaveFile();
        Debug.Log("[CollectorPatch] 已保存 CollectionSetting。");
        return Inspect();
    }
}
