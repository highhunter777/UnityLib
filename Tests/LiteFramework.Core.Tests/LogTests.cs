using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // Log 环形缓冲 / ErrorCount 是全局静态
    public sealed class LogTests
    {
        [Fact]
        public void Log_写40条_Recent恰好32条_最旧被覆盖()
        {
            for (int i = 0; i < 40; i++) Log.Info($"msg-{i}");
            Assert.Equal(32, Log.Recent.Count);
            Assert.Equal("msg-39", Log.Recent[Log.Recent.Count - 1].Message);   // 最新在尾
            Assert.Equal("msg-8", Log.Recent[0].Message);                        // 前 8 条被覆盖
        }

        [Fact]
        public void Log_Error后_ErrorCount递增()
        {
            int before = Log.ErrorCount;
            Log.Error("boom");
            Log.Fatal("bang");
            Assert.Equal(before + 2, Log.ErrorCount);
        }

        [Fact]
        public void Log_带tag_Recent条目Tag与Message分离()
        {
            Log.Info("hello", "FileSys");
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal("FileSys", last.Tag);
            Assert.Equal("hello", last.Message);   // Message 不含前缀——HUD 自行格式化
        }
    }
}
