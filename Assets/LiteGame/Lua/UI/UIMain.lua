-- UI/UIMain.lua：单界面逻辑表（M4 UI 壳消费；M3 验收 = 可寻址、可 require）
local class = require("Core.class")

local M = class("UIMain")

function M:ctor()
    self._viewName = "UIMain"
end

-- 生命周期桥（M4 由 LuaBehaviourAdapter 转发；M3 仅保证表结构）
function M:OnShow(data)
    log.info("UIMain:OnShow")
end

function M:OnHide()
    log.info("UIMain:OnHide")
end

function M:OnClick(btnId)
    log.info("UIMain:OnClick " .. tostring(btnId))
end

return M
