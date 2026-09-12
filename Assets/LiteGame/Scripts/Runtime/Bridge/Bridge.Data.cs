using System;
using System.Collections.Generic;
using cfg;

namespace LiteGame
{
    /// <summary>
    /// 服务桥根类型：Lua 全局表三件（data/ui/content）的宿主。启动时绑定成 Lua 全局表（M3），
    /// **白名单只此三件**——ILuaRegistry/玩法系统一律不导出（设计方案 §4.3 服务桥纪律）。
    /// </summary>
    public static class Bridge
    {
        private static Func<Tables> s_tables;                  // 装配期绑定：ProcedureLaunch 调 Bind，不碰容器

        /// <summary>绑定查表入口（经 IConfigService.Tables；未加载时其 Tables 访问器会抛）。</summary>
        public static void Bind(Func<Tables> tables)
            => s_tables = tables ?? throw new ArgumentNullException(nameof(tables));

        /// <summary>数据门面：查表。C# 侧强类型直返；Lua 侧（M3）经 <c>ToLuaTable</c> 转换并按行缓存。</summary>
        public static class Data
        {
            private static Tables Tables() => s_tables();

            /// <summary>
            /// 按行缓存的结构预置：key → 转换结果。M2 的 C# 行对象本身已被 Luban 表缓存，
            /// 此字典为 M3 LuaTable 转换缓存预留同构位——生成器产出时每表一对 Get/缓存字典。
            /// </summary>
            private static readonly Dictionary<int, cfg.demo.item> s_itemCache = new Dictionary<int, cfg.demo.item>(16);

            /// <summary>查道具表（demo 试验表，链路验证用）。未加载查表 = IConfigService.Tables 抛。</summary>
            public static cfg.demo.item GetItem(int id)
            {
                if (s_itemCache.TryGetValue(id, out var cached)) return cached;
                var row = Tables().Tbitem.Get(id);
                s_itemCache[id] = row;
                return row;
            }

            /// <summary>查 UI 界面注册表（M3/M4 消费：Lua路径/Prefab/层级/全屏）。</summary>
            public static cfg.uiform GetUIForm(int id) => Tables().Tbuiform.Get(id);

            // M3 桩：内部转 LuaTable 并写入 s_itemCache 式按行缓存——接 xLua 时由生成器产出实际转换体
        }
    }
}
