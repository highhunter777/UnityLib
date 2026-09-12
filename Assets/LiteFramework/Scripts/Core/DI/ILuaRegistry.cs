using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface ILuaRegistry<T>
    {
        void Fill(string name, T impl);   // C# 读注册表三件套后调用，不导出给 Lua；重复 Fill 抛
        T Get(string name);               // 未命中抛（报错信息带路径约定提示）
        bool Has(string name);
    }
}
