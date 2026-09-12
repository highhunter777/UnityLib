-- Content/DemoEntry.lua：内容处理器示例（TbContentEntry 行 Entry 列引用；M3 验收 = 可寻址、可 require）
-- 内容解释器回调壳（M4 剧情/引导解释器消费；M3 仅保证表结构）
local class = require("Core.class")

local M = class("DemoEntry")

function M:ctor()
    self._entryName = "DemoEntry"
end

function M:OnEnter(ctx)
    log.info("DemoEntry:OnEnter " .. tostring(ctx))
end

function M:OnExit()
    log.info("DemoEntry:OnExit")
end

return M
