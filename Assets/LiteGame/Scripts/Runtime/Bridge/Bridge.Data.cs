using System;
using System.Collections.Generic;
using XLua;
using cfg;

namespace LiteGame
{
    /// <summary>
    /// 服务桥根类型：Lua 全局表三件（data/ui/content）的宿主。启动时绑定成 Lua 全局表（M3 §2.5），
    /// **白名单只此三件**——ILuaRegistry/玩法系统一律不导出（设计方案 §4.3 服务桥纪律）。
    /// 绑定形态（§2.1 决策⑤）：不走生成 wrapper——门面方法以白名单委托（Func&lt;int, LuaTable&gt;）注册，
    /// 行字段在 Runtime 层手工展开（Lua 不触碰 Luban 类型，§1d）；注册表实例经 BindRegistries
    /// 装配期注入（ProcedureLaunch，唯一受信装配点）。
    /// </summary>
    public static class Bridge
    {
        private static Func<Tables> s_tables;                  // 装配期绑定：ProcedureLaunch 调 Bind，不碰容器
        private static IUILuaRegistry s_ui;
        private static IContentLuaRegistry s_content;

        /// <summary>绑定查表入口（经 IConfigService.Tables；未加载时其 Tables 访问器会抛）。</summary>
        public static void Bind(Func<Tables> tables)
            => s_tables = tables ?? throw new ArgumentNullException(nameof(tables));

        /// <summary>装配期注入注册表（ProcedureLaunch Seal 前调用；ui/content 骨架门面的查询底座）。</summary>
        public static void BindRegistries(IUILuaRegistry ui, IContentLuaRegistry content)
        {
            s_ui = ui ?? throw new ArgumentNullException(nameof(ui));
            s_content = content ?? throw new ArgumentNullException(nameof(content));
        }

        private static Tables Tables() => s_tables();

        /// <summary>数据门面：查表。C# 侧强类型直返；Lua 侧经 GetXxxLua 手工展开并按行缓存。</summary>
        public static class Data
        {
            // ---- C# 侧（强类型直返；行对象由 Luban DataMap 自缓存，不重复建 C# 缓存）----

            /// <summary>查道具表（demo 试验表，链路验证用）。</summary>
            public static cfg.demo.item GetItem(int id) => Tables().Tbitem.Get(id);

            /// <summary>查 UI 界面注册表（LuaPath/Prefab/层级/全屏）。</summary>
            public static cfg.uiform GetUIForm(int id) => Tables().Tbuiform.Get(id);

            /// <summary>查内容条目（M4 解释器消费）。</summary>
            public static cfg.contententry GetContentEntry(int id) => Tables().Tbcontententry.Get(id);

            // ---- Lua 侧入口（§2.5）：行字段手工展开为 LuaTable + 按行缓存（M2 预留同构位兑现）----
            // DevReload 时旧 env 的 LuaTable 引用全失效——ClearLuaCaches 由 §2.7 编排调用（顺序钉死）。

            private static readonly Dictionary<int, LuaTable> s_itemLua = new Dictionary<int, LuaTable>(16);
            private static readonly Dictionary<int, LuaTable> s_uiFormLua = new Dictionary<int, LuaTable>(16);

            /// <summary>Lua 侧查道具表：同 id 恒返回同一 LuaTable 实例（缓存命中）。</summary>
            public static LuaTable GetItemLua(LuaEnv env, int id)
            {
                if (s_itemLua.TryGetValue(id, out var hit)) return hit;
                var row = GetItem(id);
                var t = env.NewTable();
                t.Set("Id", row.Id);
                t.Set("Name", row.Name);
                t.Set("Desc", row.Desc);
                t.Set("Count", row.Count);
                s_itemLua[id] = t;
                return t;
            }

            /// <summary>Lua 侧查 UI 界面行：同 id 恒返回同一 LuaTable 实例。</summary>
            public static LuaTable GetUIFormLua(LuaEnv env, int id)
            {
                if (s_uiFormLua.TryGetValue(id, out var hit)) return hit;
                var row = GetUIForm(id);
                var t = env.NewTable();
                t.Set("Id", row.Id);
                t.Set("LuaPath", row.LuaPath);
                t.Set("Prefab", row.Prefab);
                t.Set("Layer", row.Layer);
                t.Set("FullScreen", row.FullScreen);
                s_uiFormLua[id] = t;
                return t;
            }

            /// <summary>DevReload 专用（§2.7）：env 重建后旧 LuaTable 引用全失效，缓存必须清空。</summary>
            public static void ClearLuaCaches()
            {
                s_itemLua.Clear();
                s_uiFormLua.Clear();
            }
        }

        /// <summary>ui 骨架门面（M3 最小集：注册表查询入口——真实语义等 M4 UI 壳，不过度设计）。</summary>
        public static class Ui
        {
            /// <summary>界面 id → uiform 行 LuaPath → UI 注册表逻辑表。未注册抛（fail-fast，§3.4）。</summary>
            public static LuaTable GetLogic(int id) => s_ui.Get(Data.GetUIForm(id).LuaPath);
        }

        /// <summary>content 骨架门面（M3 最小集：注册表查询入口）。</summary>
        public static class Content
        {
            /// <summary>内容条目 id → Entry → Content 注册表处理器表。</summary>
            public static LuaTable GetProcessor(int id) => s_content.Get(Data.GetContentEntry(id).Entry);
        }
    }
}
