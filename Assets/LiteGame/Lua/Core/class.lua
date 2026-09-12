-- Core/class.lua：元表 OOP 基类（地道 Lua 风格——设计方案 §4.4 学习口径）
-- 用法：local Base = require("Core.class"); local M = class("Name", Base)
local function class(name, base)
    local cls = { _name = name, _base = base }
    cls.__index = cls
    if base then
        setmetatable(cls, { __index = base })
    end

    function cls.new(...)
        local instance = setmetatable({}, cls)
        if instance.ctor then
            instance:ctor(...)
        end
        return instance
    end

    return cls
end

return class
