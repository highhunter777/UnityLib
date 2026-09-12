using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // FileSys.Init 静态标志 + Log.ErrorCount
    public sealed class FileSysTests : IDisposable
    {
        private readonly string _root;

        public FileSysTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "liteframework-tests", Guid.NewGuid().ToString("N"));
            FileSys.Init(new FakePathProvider(_root), new FakeJsonSerializer());
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Fact]
        public void FileSys_Json往返_写入后读回一致()
        {
            var data = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "中文值" };
            FileSys.WriteJson("save/test.json", data);
            var back = FileSys.ReadJson<Dictionary<string, string>>("save/test.json");
            Assert.Equal("v1", back["k1"]);
            Assert.Equal("中文值", back["k2"]);
        }

        [Fact]
        public void FileSys_首启_文件不存在_default静默()
        {
            Assert.Null(FileSys.ReadJson<Dictionary<string, string>>("save/absent.json"));
        }

        [Fact]
        public void FileSys_损坏JSON_default加Error可见()
        {
            FileSys.WriteAllText("save/broken.json", "{ not valid json");
            int before = Log.ErrorCount;
            var v = FileSys.ReadJson<Dictionary<string, string>>("save/broken.json");
            Assert.Null(v);
            Assert.Equal(before + 1, Log.ErrorCount);
        }

        [Fact]
        public void FileSys_TryReadJson_detail区分not_found与parse()
        {
            Assert.False(FileSys.TryReadJson<Dictionary<string, string>>("save/none.json", out _, out var d1));
            Assert.Equal("not_found", d1);

            FileSys.WriteAllText("save/bad.json", "##");
            Assert.False(FileSys.TryReadJson<Dictionary<string, string>>("save/bad.json", out _, out var d2));
            Assert.StartsWith("parse:", d2);
        }

        [Fact]
        public void FileSys_原子写_目标存在且tmp消失()
        {
            FileSys.WriteAllText("deep/dir/file.txt", "hello");
            Assert.True(FileSys.Exists("deep/dir/file.txt"));        // 首笔即建目录
            Assert.False(File.Exists(Path.Combine(_root, "deep/dir/file.txt.tmp")));
        }

        [Fact]
        public void FileSys_越界路径_抛()
        {
            Assert.Throws<ArgumentException>(() => FileSys.PathOf("..", "x.json"));   // 禁 ..
            Assert.Throws<ArgumentException>(() => FileSys.PathOf("C:", "x.json"));   // 禁盘符
            Assert.Throws<ArgumentException>(() => FileSys.PathOf("/abs", "x.json")); // 禁绝对开头
        }

        [Fact]
        public void FileSys_路径段边界_a点b合法_点段非法()
        {
            FileSys.WriteAllText("a..b.txt", "ok");                  // "a..b" 非段边界，合法
            Assert.True(FileSys.Exists("a..b.txt"));
            Assert.Throws<ArgumentException>(() => FileSys.WriteAllText("../y.txt", "no"));   // 段边界 ..
        }

        [Fact]
        public void FileSys_GetFiles_返回相对路径_统一正斜杠()
        {
            FileSys.WriteAllText("dir/one.txt", "1");
            FileSys.WriteAllText("dir/two.txt", "2");
            var files = FileSys.GetFiles("dir");
            Assert.Equal(2, files.Length);
            Assert.Contains("dir/one.txt", files);                   // 相对 RootPath，可回喂本类 API
        }

        private sealed class FakePathProvider : IPathProvider
        {
            private readonly string _root;
            public FakePathProvider(string root) => _root = root;
            public string RootPath => _root;
        }

        /// <summary>System.Text.Json 充当 serializer double（.NET 内建，模拟 Newtonsoft 实现的职责）。</summary>
        private sealed class FakeJsonSerializer : IJsonSerializer
        {
            public string Serialize<T>(T value) => JsonSerializer.Serialize(value);
            public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
        }
    }
}
