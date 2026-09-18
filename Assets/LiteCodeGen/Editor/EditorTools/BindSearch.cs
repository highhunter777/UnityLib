//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteCodeGen.EditorTools
{
    /// <summary>一次收集到的单个绑定标记信息。</summary>
    public sealed class BindMark
    {
        public BindNode Bind;
        public string MemberName;   // 生成到 C# 的成员名(=节点 GameObject 名)
        public string TypeFullName; // 生成到 C# 的类型全名
        public string Comment;      // 备注
        public int OrderIndex;      // 层级遍历顺序,保证生成结果稳定
    }

    /// <summary>
    /// 从根节点递归收集 BindNode 标记。
    /// 规则:遇到挂有 BindRoot 的子节点时不再深入(该子树独立生成)。
    /// </summary>
    public static class BindSearch
    {
        public static List<BindMark> Search(Transform root, List<string> errors)
        {
            var marks = new List<BindMark>();
            if (root == null)
            {
                return marks;
            }

            var seenNames = new HashSet<string>();
            var order = 0;

            for (var i = 0; i < root.childCount; i++)
            {
                Walk(root.GetChild(i), errors, seenNames, marks, ref order);
            }

            return marks;
        }

        private static void Walk(Transform t, List<string> errors, HashSet<string> seenNames, List<BindMark> marks,
            ref int order)
        {
            if (t == null)
            {
                return;
            }

            var isNestedRoot = t.GetComponent<BindRoot>() != null;

            if (!isNestedRoot)
            {
                var bind = t.GetComponent<BindNode>();
                if (bind != null)
                {
                    var name = t.name;
                    string reason;
                    if (!IsValidIdentifier(name, out reason))
                    {
                        errors.Add(string.Format("节点 '{0}' 不能作为成员名:{1}(可改节点名或改名后重新生成)", name, reason));
                    }
                    else if (!seenNames.Add(name))
                    {
                        errors.Add(string.Format("存在重名节点 '{0}',生成的成员名会冲突,请改名", name));
                    }
                    else
                    {
                        marks.Add(new BindMark
                        {
                            Bind = bind,
                            MemberName = name,
                            TypeFullName = bind.ResolvedTypeName,
                            Comment = bind.Comment,
                            OrderIndex = order++
                        });
                    }
                }
            }
            else
            {
                // 嵌套根:自身不入父列表,也不深入
                return;
            }

            for (var i = 0; i < t.childCount; i++)
            {
                Walk(t.GetChild(i), errors, seenNames, marks, ref order);
            }
        }

        // ---------------- C# 标识符校验 ----------------

        public static bool IsValidIdentifier(string name, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                reason = "名称为空";
                return false;
            }

            var first = name[0];
            if (!char.IsLetter(first) && first != '_')
            {
                reason = "必须以字母或下划线开头";
                return false;
            }

            for (var i = 1; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    reason = "只能包含字母/数字/下划线";
                    return false;
                }
            }

            if (IsCSharpKeyword(name))
            {
                reason = "是 C# 保留关键字";
                return false;
            }

            return true;
        }

        private static bool IsCSharpKeyword(string name)
        {
            return Keywords.Contains(name);
        }

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
            "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile",
            "while"
        };
    }
}
