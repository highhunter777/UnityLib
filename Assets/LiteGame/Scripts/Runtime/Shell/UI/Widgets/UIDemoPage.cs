using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// 控件库 Demo 页（M4c 验收：一页全展 + 每件最小 Play 断言）。
    /// Awake 运行时灰盒构建全部控件实例（控件=挂组件即用，无需专属 prefab），
    /// 随后跑自检断言并逐行打 PASS/FAIL（控制台过滤 "WidgetCheck"）。
    /// 打开方式：编辑器菜单 LiteGame/UI/Widget Demo（仅 Play 模式）。
    /// </summary>
    public class UIDemoPage : MonoBehaviour
    {
        private int _pass, _fail;

        private void Awake()
        {
            Build();
            RunChecks();
        }

        private void Build()
        {
            var content = CreateNode("Content", new Vector2(20f, -20f));
            content.sizeDelta = new Vector2(500f, 1200f);

            // ---- StateButton（多态 + 长按 + 连点保护）----
            var sbGo = CreateNode("DemoStateButton", new Vector2(0f, 0f), content);
            sbGo.sizeDelta = new Vector2(240f, 60f);
            sbGo.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f);
            var sb = sbGo.gameObject.AddComponent<StateButton>();

            // ---- TabGroup（3 页签）----
            var tabsGo = CreateNode("DemoTabs", new Vector2(0f, -90f), content);
            var tabs = tabsGo.gameObject.AddComponent<TabGroup>();
            for (int i = 0; i < 3; i++)
            {
                var tab = CreateNode("Tab" + i, new Vector2((i - 1) * 150f, 0f), tabsGo);
                tab.sizeDelta = new Vector2(130f, 50f);
                tab.gameObject.AddComponent<Image>().color = Color.white;
                tab.gameObject.AddComponent<Button>();
                tabs.Tabs.Add(tab.GetComponent<Button>());
            }

            // ---- VirtualList（数据源 30 条）----
            var listGo = CreateNode("DemoList", new Vector2(0f, -260f), content);
            listGo.sizeDelta = new Vector2(360f, 400f);
            listGo.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f);
            var vl = listGo.gameObject.AddComponent<VirtualList>();
            var tpl = CreateNode("Item", new Vector2(0f, 0f), listGo);
            tpl.sizeDelta = new Vector2(340f, 48f);
            tpl.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.3f, 0.45f);
            var tplText = CreateNode("Text", Vector2.zero, tpl);
            tplText.gameObject.AddComponent<Text>().color = Color.white;
            tpl.gameObject.SetActive(false);
            vl.Template = tpl;
            vl.SetSource(new DemoSource(30));

            // ---- ProgressBar / HpBar ----
            var pbGo = CreateNode("DemoProgress", new Vector2(0f, -480f), content);
            pbGo.sizeDelta = new Vector2(360f, 26f);
            pbGo.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f);
            var fillGo = CreateNode("Fill", Vector2.zero, pbGo);
            var fill = fillGo.gameObject.AddComponent<Image>();
            fill.color = new Color(0.3f, 0.8f, 0.4f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            ((RectTransform)fillGo.transform).sizeDelta = new Vector2(360f, 26f);
            var pb = pbGo.gameObject.AddComponent<ProgressBar>();
            pb.Fill = fill;
            pb.Set(0.7f);

            // ---- StarRating ----
            var starGo = CreateNode("DemoStars", new Vector2(0f, -540f), content);
            var stars = starGo.gameObject.AddComponent<StarRating>();
            var starList = new List<Image>(5);
            for (int i = 0; i < 5; i++)
            {
                var s = CreateNode("Star" + i, new Vector2((i - 2) * 56f, 0f), starGo);
                s.sizeDelta = new Vector2(48f, 48f);
                starList.Add(s.gameObject.AddComponent<Image>());
            }
            stars.Stars = starList.ToArray();
            stars.Set(3);

            // ---- Stepper ----
            var stepGo = CreateNode("DemoStepper", new Vector2(0f, -620f), content);
            var stepper = stepGo.gameObject.AddComponent<Stepper>();
            var minus = CreateNode("Minus", new Vector2(-120f, 0f), stepGo);
            minus.sizeDelta = new Vector2(60f, 50f);
            minus.gameObject.AddComponent<Image>().color = Color.gray;
            minus.gameObject.AddComponent<Button>();
            var val = CreateNode("Value", Vector2.zero, stepGo);
            val.gameObject.AddComponent<Text>().alignment = TextAnchor.MiddleCenter;
            ((RectTransform)val.transform).sizeDelta = new Vector2(120f, 50f);
            var plus = CreateNode("Plus", new Vector2(120f, 0f), stepGo);
            plus.sizeDelta = new Vector2(60f, 50f);
            plus.gameObject.AddComponent<Image>().color = Color.gray;
            plus.gameObject.AddComponent<Button>();
            stepper.Minus = minus.GetComponent<Button>();
            stepper.Plus = plus.GetComponent<Button>();
            stepper.ValueLabel = val.GetComponent<Text>();

            // ---- Countdown ----
            var cdGo = CreateNode("DemoCountdown", new Vector2(0f, -700f), content);
            cdGo.sizeDelta = new Vector2(200f, 40f);
            cdGo.gameObject.AddComponent<Text>().alignment = TextAnchor.MiddleCenter;
            var cd = cdGo.gameObject.AddComponent<Countdown>();
            cd.Label = cdGo.GetComponent<Text>();
            cd.StartCountdown(95f);

            // ---- CountText / FlyText / RedDot / Toast ----
            var ctGo = CreateNode("DemoCountText", new Vector2(0f, -760f), content);
            ctGo.sizeDelta = new Vector2(220f, 40f);
            ctGo.gameObject.AddComponent<Text>().alignment = TextAnchor.MiddleCenter;
            ctGo.GetComponent<Text>().text = "0";
            ctGo.gameObject.AddComponent<CountText>().Label = ctGo.GetComponent<Text>();
            ctGo.GetComponent<CountText>().Roll(999);

            var fly = CreateNode("DemoFlyText", new Vector2(0f, -820f), content);
            fly.gameObject.AddComponent<FlyTextPool>();
            var flyTpl = CreateNode("FlyTpl", Vector2.zero, fly);
            flyTpl.sizeDelta = new Vector2(240f, 36f);
            flyTpl.gameObject.AddComponent<Text>().color = new Color(1f, 0.9f, 0.4f);
            flyTpl.gameObject.SetActive(false);
            fly.GetComponent<FlyTextPool>().Template = (RectTransform)flyTpl;
            fly.GetComponent<FlyTextPool>().Show("FlyText 就绪", new Vector2(0f, 0f));

            var rdGo = CreateNode("DemoRedDot", new Vector2(0f, -880f), content);
            rdGo.sizeDelta = new Vector2(60f, 60f);
            rdGo.gameObject.AddComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f);
            var dot = CreateNode("Dot", new Vector2(20f, 20f), rdGo);
            dot.sizeDelta = new Vector2(20f, 20f);
            dot.gameObject.AddComponent<Image>().color = Color.red;
            dot.gameObject.SetActive(false);
            var redDot = rdGo.gameObject.AddComponent<RedDot>();
            var tree = new RedDotTree();
            redDot.Bind(tree, "mail");
            tree.Node("mail").SetCount(2);
        }

        private void RunChecks()
        {
            // 断言件：代码可驱动的行为面（指针类交互动画不在自测范围——测试开发方案不自动化清单）
            Check("UIDataBinder 去重", () =>
            {
                int calls = 0;
                using (var b = new UIDataBinder<int>(v => calls++))
                {
                    b.Set(1); b.Set(1); b.Set(2);
                }
                return calls == 2;
            });
            Check("StarRating 钳制与回调", () =>
            {
                var go = new GameObject("t");
                var sr = go.AddComponent<StarRating>();
                sr.Stars = new Image[5];
                int got = -1;
                sr.OnChanged += v => got = v;
                sr.Set(9);
                bool ok = sr.Value == 5 && got == 5;
                Destroy(go);
                return ok;
            });
            Check("Stepper 钳制", () =>
            {
                var go = new GameObject("t");
                var st = go.AddComponent<Stepper>();
                st.Min = 0; st.Max = 3; st.Step = 1;
                st.Set(5); st.Set(-1);
                bool ok = st.Value == 0;
                Destroy(go);
                return ok;
            });
            Check("RedDot 树传播", () =>
            {
                var tree = new RedDotTree();
                int parentCount = -1;
                tree.Node("mail").OnChanged += c => parentCount = c;
                tree.Node("mail").Node("attach").SetCount(1);
                tree.Node("mail").Node("attach").SetCount(2);
                return parentCount == 2;
            });
            Check("Countdown 到点回调", () =>
            {
                var go = new GameObject("t");
                var cd = go.AddComponent<Countdown>();
                bool done = false;
                cd.OnDone.AddListener(() => done = true);
                cd.StartCountdown(0.05f);
                cd.Tick(0.1f);                               // 确定性推进（Tick 驱动口）
                bool ok = done && !cd.Running;
                Destroy(go);
                return ok;
            });
            Check("所有权互斥：金币走绑定 + 命令式违例抛（§4.7 共存验收）", () =>
            {
                var root = new GameObject("t");
                var goldGo = new GameObject("GoldText", typeof(UnityEngine.UI.Text));
                goldGo.transform.SetParent(root.transform, false);
                var index = new UIBindIndex(new Dictionary<string, Component>
                {
                    ["GoldText"] = goldGo.GetComponent<UnityEngine.UI.Text>()
                });

                var binder = index.BindText<int>("GoldText", v => "金币 " + v);   // 绑定驱动
                binder.Set(100);
                var label = goldGo.GetComponent<UnityEngine.UI.Text>();
                bool boundWrite = label.text == "金币 100";                        // 绑定写值生效

                bool violationCaught = false;
                try { index.SetText("GoldText", "命令式改写"); }                    // 命令式 → 违例
                catch (InvalidOperationException) { violationCaught = true; }
                bool textKept = label.text == "金币 100";                          // 违例未生效（值保持）

                Destroy(root);
                return boundWrite && violationCaught && textKept;
            });
            LogSummary($"控件自检完成 PASS={_pass} FAIL={_fail}");
        }

        private sealed class DemoSource : IVirtualListSource
        {
            private readonly int _count;
            public DemoSource(int count) => _count = count;
            public int Count => _count;
            public void Bind(int index, Component item)
            {
                var t = item.GetComponentInChildren<Text>();
                if (t != null) t.text = $"条目 {index}";
            }
        }

        // ---- 构建辅助 ----

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
            catch (System.Exception ex)
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
