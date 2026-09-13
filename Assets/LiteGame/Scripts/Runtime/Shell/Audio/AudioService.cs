using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 声音壳（M4 §2.9，手册步骤 6）：**组 + 代理模型**。
    /// 组 = 类别（Effect/Ui/Voice/Bgm），每组 N 个 AudioSource 代理轮转（并发通道，同组音效可叠加）；
    /// 组音量/静音组级控制；**同优先级不被替换**——全忙时仅"更高优先级"抢占最低优先级代理，否则丢弃（日志可观测）。
    /// 句柄在代理被抢占/自然播完后失效（Stop 对失效句柄安全忽略）。
    /// </summary>
    public sealed class AudioService : IModuleStats
    {
        public enum Group { Effect, Ui, Voice, Bgm }

        private sealed class Proxy
        {
            public AudioSource Source;
            public int Priority;                           // 当前占用者的优先级（空闲 = int.MinValue）
            public int Handle;                             // 当前占用者句柄（0 = 空闲）
            public float BaseVolume = 1f;
            public bool Busy => Handle != 0 && Source != null && Source.isPlaying;
        }

        private sealed class GroupDef
        {
            public Group Group;
            public string Name;
            public float Volume = 1f;
            public bool Mute;
            public List<Proxy> Proxies = new List<Proxy>(4);
            public int Cursor;                             // 轮转游标（空闲代理公平轮转）
        }

        private readonly Dictionary<Group, GroupDef> _groups = new Dictionary<Group, GroupDef>(4);
        private readonly Transform _root;
        private int _nextHandle = 1;

        public AudioService(int effectProxies = 4, int uiProxies = 2, int voiceProxies = 2)
        {
            _root = new GameObject("[Audio]").transform;
            UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
            AddGroup(Group.Effect, "Effect", effectProxies);
            AddGroup(Group.Ui, "Ui", uiProxies);
            AddGroup(Group.Voice, "Voice", voiceProxies);
            AddGroup(Group.Bgm, "Bgm", 1);
            Log.Info("声音壳就绪:Effect/Ui/Voice/Bgm 四组（组+代理模型）", "Audio");
        }

        private void AddGroup(Group group, string name, int proxyCount)
        {
            var node = new GameObject(name).transform;
            node.SetParent(_root, false);
            var def = new GroupDef { Group = group, Name = name };
            for (int i = 0; i < proxyCount; i++)
            {
                var src = node.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                def.Proxies.Add(new Proxy { Source = src, Priority = int.MinValue, Handle = 0 });
            }
            _groups[group] = def;
        }

        /// <summary>播放：组内空闲代理轮转取用；全忙按优先级抢占（同优先级不替换，无法抢占即丢弃）。</summary>
        public int Play(Group group, AudioClip clip, float volume = 1f, bool loop = false, int priority = 0)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var g = _groups[group];

            Proxy pick = null;
            for (int i = 0; i < g.Proxies.Count; i++)
            {
                var p = g.Proxies[(g.Cursor + i) % g.Proxies.Count];
                if (!p.Busy) { pick = p; g.Cursor = (g.Cursor + i + 1) % g.Proxies.Count; break; }
            }

            if (pick == null)
            {
                Proxy steal = null;                        // 抢占目标 = 被占代理中优先级最低者
                foreach (var p in g.Proxies)
                    if (priority > p.Priority && (steal == null || p.Priority < steal.Priority))
                        steal = p;
                if (steal == null)
                {
                    Log.Info($"声音[{group}] 全忙且同优先级——丢弃（同优先级不被替换，手册 §六）", "Audio");
                    return 0;                              // 0 = 无效句柄
                }
                pick = steal;
                Log.Info($"声音[{group}] 抢占代理（新优先级 {priority} > 旧 {steal.Priority}）", "Audio");
            }

            pick.Handle = _nextHandle++;
            pick.Priority = priority;
            pick.BaseVolume = volume;
            pick.Source.clip = clip;
            pick.Source.volume = Mathf.Clamp01(volume * g.Volume);
            pick.Source.mute = g.Mute;
            pick.Source.loop = loop;
            pick.Source.Play();
            return pick.Handle;
        }

        public void Stop(int handle)
        {
            foreach (var g in _groups.Values)
                foreach (var p in g.Proxies)
                    if (p.Handle == handle)
                    {
                        p.Source.Stop();
                        p.Handle = 0;
                        p.Priority = int.MinValue;
                        return;
                    }
        }

        /// <summary>组音量（实时作用于组内全部代理；叠加音效一起变）。</summary>
        public void SetGroupVolume(Group group, float volume)
        {
            var g = _groups[group];
            g.Volume = Mathf.Clamp01(volume);
            foreach (var p in g.Proxies) p.Source.volume = Mathf.Clamp01(p.BaseVolume * g.Volume);
        }

        /// <summary>组静音（Source.mute——播放状态保留，解除静音无缝续播）。</summary>
        public void SetGroupMute(Group group, bool mute)
        {
            var g = _groups[group];
            g.Mute = mute;
            foreach (var p in g.Proxies) p.Source.mute = mute;
        }

        public bool IsGroupMuted(Group group) => _groups[group].Mute;

        public string StatsName => "Audio";

        public void Snapshot(Dictionary<string, string> into)
        {
            foreach (var kv in _groups)
            {
                int busy = 0;
                foreach (var p in kv.Value.Proxies) if (p.Busy) busy++;
                into[$"{kv.Value.Name}{(kv.Value.Mute ? "(静音)" : "")}"] = $"{busy}/{kv.Value.Proxies.Count}";
            }
        }
    }
}
