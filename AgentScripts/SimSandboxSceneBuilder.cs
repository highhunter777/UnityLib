using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

// 一次性场景构建器（《M8实施指导》§2.7 灰盒沙盒场景）：经 unity-pipeline run_script 执行。
// 安全约定：若编辑器当前场景有未保存修改（isDirty）则中止（返回 2），避免 NewScene 静默丢弃用户内容。
public static class SimSandboxSceneBuilder
{
    public static int Build()
    {
        var cur = EditorSceneManager.GetActiveScene();
        if (cur.isDirty)
        {
            Debug.LogError("[SimSandboxSceneBuilder] 当前场景有未保存修改，请先保存再重试。");
            return 2;
        }

        string originalPath = cur.path;
        const string dir = "Assets/LiteGame/Scenes";
        Directory.CreateDirectory(dir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 光
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 相机：俯视射击区（灰盒调试视角；非产品相机）
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
        cam.transform.position = new Vector3(10f, 30f, -30f);
        cam.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

        // 灰盒地板（仅视觉半；判定面/边界在 SimMapData 内部）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);

        // Canvas + FlyTextPool + 非激活模板（SimSandbox 缺失时自动降级为纯 Gizmos）
        // ConstantPixelSize（无 CanvasScaler）→ anchoredPosition == 屏幕像素坐标，与沙盒换算一致
        var canvasGo = new GameObject("Canvas", typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var poolGo = new GameObject("FlyTextPool", typeof(LiteGame.UI.FlyTextPool));
        poolGo.transform.SetParent(canvasGo.transform, false);
        var pool = poolGo.GetComponent<LiteGame.UI.FlyTextPool>();

        var tplGo = new GameObject("Template", typeof(RectTransform), typeof(TextMeshProUGUI));
        var tplRt = (RectTransform)tplGo.transform;
        tplRt.SetParent(poolGo.transform, false);
        tplRt.anchorMin = Vector2.zero;
        tplRt.anchorMax = Vector2.zero;
        tplRt.pivot = Vector2.zero;
        tplRt.sizeDelta = new Vector2(200f, 40f);
        var txt = tplGo.GetComponent<TextMeshProUGUI>();
        txt.fontSize = 28f;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        txt.text = "0";
        tplGo.SetActive(false);
        pool.Template = tplRt;

        string path = dir + "/SimSandbox.unity";
        if (!EditorSceneManager.SaveScene(scene, path))
        {
            Debug.LogError("[SimSandboxSceneBuilder] SaveScene 失败: " + path);
            return 1;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[SimSandboxSceneBuilder] 场景已保存: " + path);

        // 还原编辑器原活动场景（未脏 → OpenScene 不会弹保存对话）
        if (!string.IsNullOrEmpty(originalPath))
            EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);

        return 0;
    }
}
