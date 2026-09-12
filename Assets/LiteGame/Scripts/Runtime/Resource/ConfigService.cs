using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Luban;
using cfg;

namespace LiteGame
{
    public interface IConfigService
    {
        Tables Tables { get; }                     // Luban 生成物，cfg 命名空间；未加载访问抛
        bool Loaded { get; }
        UniTask LoadAsync(CancellationToken ct);   // 走 AssetService（UniTask 适配层），完成即放行——签名对齐 §7.7
    }

    /// <summary>
    /// 配置加载薄壳（DI 单例，ProcedureLaunch 注册只注册不加载，ProcedurePreload 尾部 LoadAsync 放行）。
    /// 契约（设计方案 §5.2"松"纪律）：
    /// ① 本类不感知资源方案——构造收字节委托 `Func&lt;string, UniTask&lt;byte[]&gt;&gt;`（装配点绑定 AssetService.LoadRawFileBytesAsync）；
    /// ② 表数据文件清单显式登记在 <see cref="TableDataFiles"/>——"缺一个 .bytes 报错明确"由逐文件预取实现
    ///    （报错带完整 location；新增 Luban 表时此清单加一行，将来随 Bridge.data 生成器自动产出）；
    /// ③ Tables 构造是同步的（Luban 生成物签名）——异步预取全部 bytes 后再同步建表，loader 从缓存取；
    /// ④ 加载失败 fail-fast 抛（损坏/缺失不静默），由流程 Fail() 接——存档损坏不能炸启动，配置缺失必须炸。
    /// </summary>
    public sealed class ConfigService : IConfigService
    {
        /// <summary>表数据收集目录（YooAsset location 前缀；跨平台恒为 Assets 路径，与物理盘符无关）。</summary>
        public const string DataDir = "Assets/LiteGame/RawFile/Config/";

        /// <summary>gen.bat 第一遍产出的表数据文件名（RawFile/Config 下，不带扩展名）——与 Tables.cs 的 loader 键一一对应。</summary>
        public static readonly string[] TableDataFiles =
        {
            "demo_tbitem",
            "tbuiform",
            "tbcontententry",
            "tbstrategy",
        };

        private readonly Func<string, CancellationToken, UniTask<byte[]>> _bytesProvider;
        private Tables _tables;

        public ConfigService(Func<string, CancellationToken, UniTask<byte[]>> bytesProvider)
        {
            _bytesProvider = bytesProvider ?? throw new ArgumentNullException(nameof(bytesProvider));
        }

        public bool Loaded => _tables != null;

        public Tables Tables
        {
            get
            {
                if (_tables == null)
                    throw new InvalidOperationException("配置未加载——LoadAsync 完成前禁止查表（ProcedurePreload 尾部放行）");
                return _tables;
            }
        }

        public async UniTask LoadAsync(CancellationToken ct)
        {
            if (_tables != null) return;                   // 幂等：重复 Load 直接返回

            var cache = new Dictionary<string, byte[]>(TableDataFiles.Length);
            foreach (string file in TableDataFiles)
            {
                ct.ThrowIfCancellationRequested();
                string location = $"{DataDir}{file}.bytes";
                try
                {
                    cache[file] = await _bytesProvider(location, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 报错带 location 与根因——"删掉一个 .bytes → LoadAsync 报错明确"的自测即此路径
                    throw new InvalidOperationException($"配置文件加载失败:{location}", ex);
                }
            }

            _tables = new Tables(file => new ByteBuf(cache[file]));   // Luban 同步建表，loader 查预取缓存
            Log.Info($"配置加载完成:{TableDataFiles.Length} 张表", "Config");
        }
    }
}
