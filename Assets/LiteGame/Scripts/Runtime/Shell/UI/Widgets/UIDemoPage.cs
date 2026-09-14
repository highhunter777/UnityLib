using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// 控件库 Demo 页（M4c 验收：一页全展 + 最小断言）。
    /// **批⑤ 改造（2026-09-14）：代码构建 → 模板 prefab 实例化**。
    /// 模板来自 `Assets/LiteGame/UI/Widgets/`（由 `LiteGame.Editor/WidgetPrefabBuilder` 确定性生成，25 件）；
    /// 模板件的结构/行为断言已归 `WidgetPrefabCheck`（编辑态 + Play 态 25/25 PASS）——本页只保留
    /// **与模板无关**的两条（UIDataBinder 去重、绑定/命令式所有权互斥），并负责"一页全展"。
    /// 加载：编辑器用 AssetDatabase（dev 页）；真机走 YooAsset 地址加载（待收集组，规划 §8.4 待办）。
    /// </summary>
    public class UIDemoPage : MonoBehaviour
    {
        /// <summary>模板清单（与构建器产物一一对应）。</summary>
        private static readonly string[] Templates =
        {
            "StateButton", "Dialog", "Toast", "Bubble",
            "VirtualList", "SimpleList", "TabGroup", "BottomNav",
            "ProgressBar", "HpBar", "StarRating", "CountText", "Countdown", "AnimatedImage", "AvatarFrame",
            "InputField", "Slider", "Toggle", "Dropdown", "Stepper",
            "RedDot", "FlyText", "GuideHighlight", "EventRelay", "SafeArea",
        };

        private int _pass, _fail;

        private void Awake()
        {
            BuildFromTemplates();
            RunChecks();
        }

        // ---------------- 一页全展（模板实例化） ----------------

        private void BuildFromTemplates()
        {
            var content = CreateNode("Content", new Vector2(20f, -20f));
            content.sizeDelta = new Vector2(1280f, 2800f);

            const float colW = 420f;
            const float rowH = 330f;
            int col = 0, row = 0, missing = 0;

            foreach (var name in Templates)
            {
                var inst = LoadTemplate(name);
                if (inst == null) { missing++; continue; }
                var rt = (RectTransform)inst.transform;
                rt.SetParent(content, false);
                rt.anchoredPosition = new Vector2(col * colW, -row * rowH);
                if (++col >= 3) { col = 0; row++; }
            }

            Log($"模板实例化 {Templates.Length - missing}/{Templates.Length}"
                + (missing > 0 ? $"（缺 {missing} 件——先跑菜单 LiteGame/UI/构建控件模板 Prefabs）" : ""));

            DriveSampleState(content);
        }

        /// <summary>少量"看得见状态"的驱动（纯展示；断言在 WidgetPrefabCheck 里跑）。</summary>
        private void DriveSampleState(Transform content)
        {
            var hp = content.GetComponentInChildren<HpBar>(true);
            if (hp != null) hp.Set(65f, 100f);

            var bar = content.GetComponentInChildren<ProgressBar>(true);
            if (bar != null) bar.Set(0.45f);

            var stars = content.GetComponentInChildren<StarRating>(true);
            if (stars != null) stars.Set(4);

            var step = content.GetComponentInChildren<Stepper>(true);
            if (step != null) step.Set(3);

            var vl = content.GetComponentInChildren<VirtualList>(true);
            if (vl != null) vl.SetSource(new DemoSource(12));

            var dot = content.GetComponentInChildren<RedDot>(true);
            if (dot != null)
            {
                var tree = new RedDotTree();
                dot.Bind(tree, "mail");
                tree.Node("mail").SetCount(3);
            }

            var fly = content.GetComponentInChildren<FlyTextPool>(true);
            if (fly != null) fly.Show("飘字演示", new Vector2(0f, 40f));

            var cd = content.GetComponentInChildren<Countdown>(true);
            if (cd != null) cd.StartCountdown(60f);
        }

        private static GameObject LoadTemplate(string name)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/LiteGame/UI/Widgets/{name}.prefab");
            return prefab != null ? Instantiate(prefab) : null;
#else
            // 真机：待 YooAsset 收集组（Assets/LiteGame/UI/）与地址加载接入——规划 §8.4 待办
            Log($"模板 {name} 未接入运行时加载（收集组待办）");
            return null;
#endif
        }

        // ---------------- 断言（只留与模板无关者） ----------------

        private void RunChecks()
        {
            // 说明：StarRating 钳制 / Stepper 钳制 / RedDot 树传播 / Countdown 到点 等**模板相关**断言
            // 已迁至 WidgetPrefabCheck（模板实例化路径）；此处只保留模板无关的两条。
            Check("UIDataBinder 去重", () =>
            {
                int calls = 0;
                using (var b = new UIDataBinder<int>(v => calls++))
                {
                    b.Set(1); b.Set(1); b.Set(2);
                }
                return calls == 2;
            });
            Check("所有权互斥：金币走绑定 + 命令式违例抛（§4.7 共存验收）", () =>
            {
                var root = new GameObject("t");
                var goldGo = new GameObject("GoldText", typeof(Text));
                goldGo.transform.SetParent(root.transform, false);
                var index = new UIBindIndex(new Dictionary<string, Component>
                {
                    ["GoldText"] = goldGo.GetComponent<Text>()
                });

                var binder = index.BindText<int>("GoldText", v => "金币 " + v);   // 绑定驱动
                binder.Set(100);
                var label = goldGo.GetComponent<Text>();
                bool boundWrite = label.text == "金币 100";

                bool violationCaught = false;
                try { index.SetText("GoldText", "命令式改写"); }
                catch (InvalidOperationException) { violationCaught = true; }
                bool textKept = label.text == "金币 100";

                Destroy(root);
                return boundWrite && violationCaught && textKept;
            });
            LogSummary($"控件自检完成 PASS={_pass} FAIL={_fail}（模板件断言见 WidgetTemplateCheck）");
        }

        private sealed class DemoSource : IVirtualListSource
        {
            private readonly int _count;
            public DemoSource(int count) => _count = count;
            public int Count => _count;
            public void Bind(int index, Component item)
            {
                var t = item.GetComponentInChildren<TMP_Text>();
                if (t != null) t.text = $"条目 {index}";
            }
        }

        // ---------------- 构建辅助 ----------------

        private RectTransform CreateNode(string name, Vector2 pos, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent != null ? parent : transform, false);
            rt.anchoredPosition = pos;
            return rt;
        }

        private void Check(string name, Func<bool> assertion)
        {
            bool ok;
            try { ok = assertion(); }
            catch (Exception ex)
            {
                Log($"FAIL {name}（异常 {ex.GetType().Name}:{ex.Message}）");
                _fail++;
                return;
            }
            if (ok) { _pass++; Log($"PASS {name}"); }
            else { _fail++; Log($"FAIL {name}"); }
        }

        private void LogSummary(string message) => LiteFramework.Log.Info(message, "WidgetCheck");
        private void Log(string message) => LiteFramework.Log.Info(message, "WidgetCheck");
    }
}
