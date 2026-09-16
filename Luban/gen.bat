cd /d %~dp0
set LUBAN_EXE=%~dp0LubanGenerater\Luban\Luban.exe

rem Pass 1: C# typed code + binary data (M2 config pipeline)
%LUBAN_EXE% ^
    -t client ^
    -c cs-bin ^
    -d bin ^
    --conf %~dp0luban.conf ^
    -x outputCodeDir=E:\unityProject\Test\Assets\GameData\Generated ^
    -x outputDataDir=E:\unityProject\Test\Assets\LiteGame\RawFile\Config

rem Pass 2: Lua data tables (consumed by Bridge.data in M3)
%LUBAN_EXE% ^
    -t client ^
    -d lua ^
    --conf %~dp0luban.conf ^
    -x outputDataDir=E:\unityProject\Test\Assets\LiteGame\Lua\cfg

rem Pass 3: registry Lua paths -> LuaKeys.g.cs (M3, fail on unknown root)
python gen_lua_keys.py
if %errorlevel% neq 0 exit /b %errorlevel%
