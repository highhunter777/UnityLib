using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>控件库 Demo 页入口（M4c 验收：一页全展）。仅 Play 模式——运行时灰盒构建 + 自检断言。</summary>
    public static class WidgetDemoMenu
    {
        [MenuItem("LiteGame/UI/Widget Demo")]
        private static void Open()
        {
            if (!Application.isPlaying)
            {
                LiteFramework.Log.Warning("Widget Demo 仅 Play 模式可用", "UI");
                return;
            }
            var canvasGo = new GameObject("[WidgetDemo]", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<LiteGame.UI.UIDemoPage>();
            Debug.Log("[WidgetDemo] Demo 页已创建——控制台过滤 WidgetCheck 看自检结果", canvasGo);
        }
    }
}
