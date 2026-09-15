-- Core/eventer.lua：闭包事件器（on 返回注销委托——与 C# 事件桥同款语义）
-- 用法：local ev = require("Core.eventer")(); local off = ev:on("evt", fn); off()
-- 分工（2026-09-15 审计补注）：本文件只用于 **Lua 内部局部信号**（emit 直调、无 pcall）；
-- 订阅 C# 事件必须走全局 events.on（EventBridge，逐回调 SafeCall）——两者勿混用。
-- 注意 emit 无异常隔离：回调抛错会中断本事件链（需要隔离的信号应升级走 C# 事件桥）。
local function eventer()
    local listeners = {}                        -- name -> { fn, ... }

    local function on(name, fn)
        assert(type(fn) == "function", "eventer:on 需要函数")
        listeners[name] = listeners[name] or {}
        table.insert(listeners[name], fn)

        -- 返回注销委托（闭包配对）
        return function()
            local list = listeners[name]
            if not list then return end
            for i, f in ipairs(list) do
                if f == fn then
                    table.remove(list, i)
                    break
                end
            end
        end
    end

    local function emit(name, ...)
        local list = listeners[name]
        if not list then return end
        for _, fn in ipairs(list) do
            fn(...)                              -- M3 事件桥接管 pcall 后，纯 Lua 侧保持直调
        end
    end

    return { on = on, emit = emit }
end

return eventer
