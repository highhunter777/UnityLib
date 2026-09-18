# LiteCodeGen —— 独立版绑定代码生成

> 功能思路源自 QFramework 的 UIKit / CodeGenKit(MIT License,© 2016~2025 liangxiegame)。
> 本包把它抽成**零 QFramework 依赖**的自包含 Unity 编辑器工具:打标记 → 生成 partial 绑定代码 → 编译后自动把引用回填进 Prefab/场景。生成的代码只依赖 `UnityEngine`,主类基类可配置成你自己的框架基类(如 GameFramework 的 `UIFormLogic` / `EntityLogic`)。

## 适用

- Unity **2019.1 及以上**(建议 2020.3+)。
- 想用"打标记自动生成字段绑定代码"这套工作流,但不想引入整套 QFramework。
- **不限于 UI**:任意 GameObject 层级都能用(UI 面板、角色、实体、场景物件皆可)。工具本身不检查 Canvas / RectTransform / UGUI,只是自动识别清单以 UGUI 组件优先。

## 安装

把 `LiteCodeGen/` 整个文件夹拷进任意 Unity 工程的 `Assets/` 下即可(含 `Runtime/`、`Editor/` 两个 asmdef)。

## 核心概念

| 组件 | 作用 |
| --- | --- |
| `BindRoot` | 挂在**生成根节点**上,记录 命名空间 / 脚本名 / 输出目录 / 主类基类。一次生成的边界。 |
| `BindNode` | 挂在你想在代码里拿到的**子节点**上。自动识别该节点上的组件作为字段类型(识别优先级同 QFramework),也可手动指定某个组件或 `GameObject`。 |

生成的代码约定:

```
输出目录/
├─ Player.cs            ← 逻辑文件,只在第一次生成,可放心手改(partial)
└─ Player.Designer.cs   ← 绑定字段,每次生成都会整体重写(partial,勿手改)
```

生成的字段形如:

```csharp
// Player.Designer.cs(自动生成)
public partial class Player
{
    public UnityEngine.Transform   Weapon;
    public UnityEngine.Animator    Anim;
    public MyGame.HealthBar        Health;
}
```

## 使用步骤(推荐:Prefab 工作流)

1. **选中 Prefab**,在 Project 面板执行菜单 `Assets > LiteCodeGen > 生成绑定代码`,或双击进入 Prefab 编辑模式;
2. 给根节点挂 `BindRoot`(层级菜单 `GameObject > LiteCodeGen > 添加 BindRoot(生成根)`);
3. 在 `BindRoot` 上设置:
   - **ScriptName**:类名(默认取根节点名,留空自动);
   - **Namespace**:如 `MyGame.Entity`;
   - **ScriptsFolder**:输出目录,如 `Assets/Scripts/Generated`(可直接把文件夹拖到 Inspector 的"输出目录"栏);
   - **BaseClassFullName**:主类基类完整名,如 `UnityGameFramework.Runtime.EntityLogic`;**留空 = `MonoBehaviour`**;
4. 给需要绑定的子节点逐个挂 `BindNode` 标记(选中节点后菜单 `GameObject > LiteCodeGen > 添加 BindNode 标记`,或在节点组件上右键);
5. 点 `BindRoot` Inspector 上的 **① 生成代码**;
6. 脚本编译完成后自动完成:把生成的脚本组件挂上根节点、按标记把引用回填进 Prefab(场景里所有使用该 Prefab 的实例同步生效);
7. 在自动打开的 `Player.cs` 里写逻辑,直接用 `Weapon` 等字段。

改动结构后:增删节点/标记 → 再点一次"生成代码"(Designer 全量重写,逻辑文件不动)→ 自动回填。若只想重新回填不改代码,点 **② 回填引用**。

## 场景(非 Prefab)快速原型工作流

场景里直接搭根节点 → 挂 `BindRoot` + 子节点 `BindNode` → 点"生成代码"。编译完成后会自动定位场景对象并回填(同名多实例时无法区分会提示手动点"回填引用")。**注意保存场景,引用才会持久化。**

## 类型选择规则

- 自动识别优先级(依次匹配,命中即用):
  1. UGUI / TMP:`ScrollRect / InputField / TMP_InputField / TextMeshProUGUI / TextMeshPro / Dropdown / Button / Text / RawImage / Toggle / Slider / Scrollbar / Image / ToggleGroup`
  2. 物理:`Rigidbody / Rigidbody2D / BoxCollider2D / BoxCollider / CircleCollider2D / SphereCollider / MeshCollider / Collider / Collider2D`
  3. 其它:`Animator / Canvas / Camera / MeshRenderer / SpriteRenderer / ParticleSystem / ParticleSystemRenderer`
  4. 兜底:`RectTransform` → `Transform`
- 手动:在 `BindNode` Inspector 下拉里选节点上的任意组件,或选 `GameObject`。
- 注意 `SkinnedMeshRenderer`、`AudioSource`、`Light`、`TrailRenderer`、`LineRenderer`、`Terrain` 等不在自动清单里,只有它们时会退化成 `Transform`,需手动指定。

## 约定与注意

- 绑定节点的 **GameObject 名 = 生成的字段名**,所以节点名必须是合法 C# 标识符;且**整棵树内不能重名**(不只是同层,否则生成中止并报错)。
- 你的主类必须与生成的 Designer **同 namespace、同名、且为 `partial`**(工具在首次生成时已按此写好)。
- 若你在自己的框架里已写了主类文件(例如 `public partial class Player : MyBase`),工具只补 Designer,不会覆盖它。
- **`CustomTypeName` 必须填完整类型名**(含命名空间)。Designer 文件里没有任何 `using`,写短名会编译不过。
- 字段类型必须是 `UnityEngine.Object` 子类才能被 Unity 序列化,否则生成能过但回填后引用为空(自定义 `MonoBehaviour` 可以,纯 C# 类不行)。
- 生成代码放在 asmdef 程序集时,若字段用到 UGUI/TMP 类型,需给该程序集加上对应引用(UnityEngine.UI、Unity.TextMeshPro)。
- 生成的脚本组件是直接挂到根节点上的;若基类不是 `MonoBehaviour` 子类,自动挂载会失败并给出提示。

## 文件结构

```
LiteCodeGen/
├─ README.md
├─ Runtime/
│  ├─ LiteCodeGen.Runtime.asmdef
│  ├─ BindNode.cs              子标记组件(自动识别组件类型)
│  └─ BindRoot.cs              根标记(生成配置)
└─ Editor/
   ├─ LiteCodeGen.Editor.asmdef
   ├─ BindMenus.cs             各种菜单入口
   ├─ Inspectors/
   │  ├─ BindRootInspector.cs
   │  └─ BindNodeInspector.cs
   └─ EditorTools/
      ├─ BindSearch.cs         递归收集标记 + 标识符校验
      ├─ CodeBuilder.cs        缩进代码写器
      ├─ BindTemplates.cs      主文件 / Designer 模板
      ├─ BindGenerator.cs      生成编排 + 上下文判定(prefab/场景)
      ├─ BindSerializer.cs     把引用写回序列化字段 + prefab 资产读写
      └─ BindPostCompile.cs    编译后自动回填队列([DidReloadScripts])
```

## 常见问题

- **报"找不到已编译的脚本类型"**:脚本编译没过(看 Console 的红错),或 Namespace/类名与已存在的文件不一致。
- **报错说字段找不到**:刚加了新节点但还没编译,重新点一次"生成代码"等编译完成即可。
- **重复回填会不会出问题**:不会,回填是幂等的(按标记覆盖同名序列化字段)。
- **想彻底脱离 MonoBehaviour 继承**:把 `BaseClassFullName` 填成你自己的组件基类(必须继承 MonoBehaviour,否则挂不上)。
- **Unity 要求 MonoBehaviour 脚本文件名与类名一致**,所以 `BindRoot.cs` / `BindNode.cs` 的文件名不要改。
