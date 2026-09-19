# ─────────────────────────────────────────────────────────────────────────────
# L2 Unity 侧门禁（2026-09-15）
#
# 为什么需要 L2：L1（dotnet 单测）按路径 glob 编译、不读 .meta，也不经 Unity 编译器——
#   2026-09-15 那次 16 个非法 GUID 的 .meta 事故里 L1 全绿而 Unity 编译是坏的。
# L2 补这一层：① 非法 meta/GUID 扫描（纯文件，秒级）② Unity 侧编译/诊断状态。
#
# 两种模式（自动选择）：
#   A. 编辑器正在运行（检测 Library/Pipeline/.unity-pipeline-port）→ 经 Unity Pipeline
#      查询 recompile_status / console_status —— **无需关闭编辑器**。
#   B. 编辑器未运行 → 退回 batchmode `unity test --mode EditMode`（需已激活授权；
#      且必须没有其它实例占用该工程）。
#
# 依赖：Unity CLI（`unity`）在 PATH；本机 Unity 编辑器路径见 -UnityExe。
# 用法（Windows PowerShell 5.1 亦可，本机未装 pwsh）：
#   powershell -NoProfile -File scripts/l2-unity-gate.ps1                  # 全量（默认）
#   powershell -NoProfile -File scripts/l2-unity-gate.ps1 -MetaScanOnly    # 只跑①（无 Unity 环境也能用）
#   powershell -NoProfile -File scripts/l2-unity-gate.ps1 -RunEditModeTests # 强制走 B（batchmode 测试）
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$UnityExe = 'E:\unity3d\2022.3.55f1c1\Editor\Unity.exe',
    [string]$TestOutput = 'TestResults\editmode-results.xml',
    [switch]$MetaScanOnly,
    [switch]$RunEditModeTests
)

# 工程路径：未显式给出时按脚本位置推导（$PSScriptRoot 在参数默认值阶段可能为空，故放体内）
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrWhiteSpace($here)) { $here = (Get-Location).Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..')).Path
}

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]

function Write-Step($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "  [PASS] $t" -ForegroundColor Green }
function Write-Bad($t)  { Write-Host "  [FAIL] $t" -ForegroundColor Red; $failures.Add($t) }
function Write-Note($t) { Write-Host "  [INFO] $t" -ForegroundColor Yellow; $notes.Add($t) }

Write-Host "L2 Unity 侧门禁 | 工程: $ProjectPath" -ForegroundColor White

# ── ① 非法 meta/GUID 扫描（无需 Unity；防"Unity 拒绝导入资源"类故障）────────────
Write-Step '① 非法 meta GUID 扫描'
$scanRoots = @('Assets', 'Packages')
$guidRegex = [regex]'^guid:\s*([0-9a-fA-F]{32})\s*$'
$metaCount = 0
$badMetas = New-Object System.Collections.Generic.List[string]
foreach ($root in $scanRoots) {
    $rootPath = Join-Path $ProjectPath $root
    if (-not (Test-Path $rootPath)) { continue }
    $files = Get-ChildItem -Path $rootPath -Filter '*.meta' -Recurse -File -ErrorAction SilentlyContinue
    foreach ($f in $files) {
        $metaCount++
        $reader = New-Object System.IO.StreamReader($f.FullName)
        try {
            $guid = $null; $line = $null; $i = 0
            while ($i -lt 8 -and ($line = $reader.ReadLine()) -ne $null) {
                $i++
                if ($line.StartsWith('guid:')) { $guid = $line.Trim(); break }
            }
        } finally { $reader.Close() }
        if ($null -eq $guid) { $badMetas.Add("$($f.FullName)  (缺少 guid 行)"); continue }
        if (-not $guidRegex.IsMatch($guid)) { $badMetas.Add("$($f.FullName)  ($guid)") }
    }
}
if ($badMetas.Count -eq 0) { Write-Ok "扫描 $metaCount 个 .meta：GUID 全部为合法 32 位 hex" }
else {
    Write-Bad "发现 $($badMetas.Count) 个非法 GUID 的 .meta（Unity 会拒绝导入 → 类型在编译中消失）"
    $badMetas | Select-Object -First 20 | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkRed }
    Write-Host "        修复：重写为 uuid4().hex（32 位 hex），旧 GUID 因非法从未被引用，改后无副作用" -ForegroundColor DarkGray
}

if ($MetaScanOnly) {
    Write-Step '汇总（MetaScanOnly）'
    if ($failures.Count -eq 0) { Write-Host '  L2(meta) 通过' -ForegroundColor Green; exit 0 }
    Write-Host "  L2(meta) 失败：$($failures.Count) 项" -ForegroundColor Red; exit 1
}

# ── ② Unity 侧：编译/诊断状态 ────────────────────────────────────────────────
$descriptor = Join-Path $ProjectPath 'Library\Pipeline\.unity-pipeline-port'
$unityAvailable = $null -ne (Get-Command 'unity' -ErrorAction SilentlyContinue)

function Invoke-PipelineCommand([string]$command, [string[]]$cmdArgs) {
    # 命令名与参数必须分开传：`unity command run_tests mode=EditMode`
    if ($cmdArgs -and $cmdArgs.Count -gt 0) {
        $out = & unity command $command @cmdArgs --project-path $ProjectPath 2>&1
    }
    else {
        $out = & unity command $command --project-path $ProjectPath 2>&1
    }
    if ($LASTEXITCODE -ne 0) { throw "unity command $command $($cmdArgs -join ' ') 失败: $($out -join ' ')" }
    return ($out -join "`n")
}

# 从 Pipeline 的 JSON 结果里取整数字段（取不到返回 -1，便于区分"0 条"与"解析失败"）
function Get-JsonInt([string]$text, [string]$key) {
    $m = [regex]::Match($text, '"' + $key + '"\s*:\s*(\d+)')
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return -1
}

# 新鲜度守卫用：Assets 下最新 .cs 的写入时间 / Library/ScriptAssemblies 下最新 .dll 的写入时间
function Get-NewestSourceTime {
    $files = Get-ChildItem -Path (Join-Path $ProjectPath 'Assets') -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue
    if (-not $files) { return [datetime]::MinValue }
    return ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
}

function Get-NewestAssemblyTime {
    $dir = Join-Path $ProjectPath 'Library/ScriptAssemblies'
    if (-not (Test-Path $dir)) { return [datetime]::MinValue }
    $dlls = Get-ChildItem -Path $dir -Filter *.dll -File -ErrorAction SilentlyContinue
    if (-not $dlls) { return [datetime]::MinValue }
    return ($dlls | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
}

if ((Test-Path $descriptor) -and -not $RunEditModeTests) {
    Write-Step '② Unity 侧状态（编辑器在跑 → 经 Pipeline，无需关闭编辑器）'
    if (-not $unityAvailable) {
        Write-Note '未找到 unity CLI（PATH），跳过 Unity 侧检查'
    }
    else {
        $desc = Get-Content $descriptor -Raw | ConvertFrom-Json
        Write-Note "已连接编辑器：port $($desc.port) / pid $($desc.pid) / $($desc.unityVersion)"
        try {
            # ③ 新鲜度守卫（2026-09-19 加）：**先把源码刷进程序集，再谈编译状态**
            # 坑：外部改 .cs 后 Unity 不会自动导入（AssetDatabase 未 Refresh）→
            #     recompile_status 仍报 up_to_date、EditMode 用例跑的是**旧程序集** → L2 假绿。
            #     对策：无条件 Refresh(ForceSynchronousImport) + RequestScriptCompilation 并等编译收敛
            #     （幂等：无改动时几秒内返回）。
            $before = Get-NewestSourceTime
            Invoke-PipelineCommand 'eval' @('UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();') | Out-Null
            $waited = 0
            while ($waited -lt 90) {
                Start-Sleep -Seconds 3
                $waited += 3
                $busy = Invoke-PipelineCommand 'eval' @('return UnityEditor.EditorApplication.isCompiling ? 1 : 0;')
                if ($busy -notmatch '"result":\s*"?1') { break }
            }
            $after = Get-NewestAssemblyTime
            if ($before -gt $after) {
                Write-Bad "源码新于程序集（最新源码 $($before.ToString('HH:mm:ss')) > 最新程序集 $($after.ToString('HH:mm:ss'))）——Unity 未重建，EditMode 用例会跑旧代码。请检查编译或手工 Refresh"
            }
            else {
                Write-Ok "程序集新鲜（源码 ≤ 程序集：$($before.ToString('HH:mm:ss')) ≤ $($after.ToString('HH:mm:ss'))）"
            }

            $rec = Invoke-PipelineCommand 'recompile_status'
            Write-Host "  recompile_status: $($rec.Trim())" -ForegroundColor DarkGray
            if ($rec -match '"compilationFailed"\s*:\s*true' -or $rec -match '"failed"\s*:\s*true') { Write-Bad 'Unity 编译失败（recompile_status.failed/compilationFailed=true）' }
            else { Write-Ok 'Unity 编译状态正常（无编译失败）' }

            # 精确判定：只有消息里出现 "error CS####" 才算编译错误；
            # 其它 error 级条目（如第三方程序集加载告警）仅提示，不判失败（否则误报）
            $con = Invoke-PipelineCommand 'console'
            if ($con -match 'error CS\d+') {
                $m = [regex]::Match($con, 'error CS\d+[^"\\]*')
                Write-Bad "控制台存在编译错误：$($m.Value.Trim())"
            }
            else {
                $errCount = ([regex]::Matches($con, '"level":"error"')).Count
                Write-Ok "控制台无编译错误（error 级条目 $errCount 条，均非 CS 编译错误）"
                if ($errCount -gt 0) { Write-Note "有 $errCount 条非编译类 error（如程序集加载告警）；明细：unity command console --project-path `"$ProjectPath`"" }
            }

            # ③ Unity EditMode 用例（#27/#28）：经 Pipeline 直接跑，编辑器无需关闭。
            # 用例在 Assets/Tests/EditMode（IEEE 基线逐位对账 + UI 模板/资源完整性）；
            # Total=0 视为失败——否则"测试程序集没编进来"会静默通过。
            $rt = Invoke-PipelineCommand 'run_tests' @('mode=EditMode')
            $total  = Get-JsonInt $rt 'Total'
            $passed = Get-JsonInt $rt 'Passed'
            $failed = Get-JsonInt $rt 'Failed'
            Write-Host "  run_tests(EditMode): Total=$total Passed=$passed Failed=$failed" -ForegroundColor DarkGray
            if ($total -le 0) { Write-Bad 'EditMode 用例数 = 0（测试程序集未编入？检查 Assets/Tests/EditMode 的 asmdef 与 UNITY_INCLUDE_TESTS）' }
            elseif ($failed -gt 0) { Write-Bad "EditMode 用例失败 $failed 项（Total=$total）——明细：unity command run_tests mode=EditMode --project-path `"$ProjectPath`"" }
            else { Write-Ok "EditMode 用例全绿（$passed/$total）" }
        }
        catch { Write-Bad "Pipeline 命令执行失败：$($_.Exception.Message)" }
    }
}
elseif ($RunEditModeTests -or -not (Test-Path $descriptor)) {
    Write-Step '② Unity 侧 EditMode 测试（batchmode）'
    if (Get-Process -Name 'Unity' -ErrorAction SilentlyContinue) {
        Write-Bad '检测到 Unity 进程在运行：batchmode 无法与已打开的编辑器共用同一工程。请关闭编辑器后重跑（或直接跑默认模式走 Pipeline）'
    }
    elseif (-not (Test-Path $UnityExe)) {
        Write-Bad "未找到编辑器：$UnityExe（用 -UnityExe 指定）"
    }
    elseif (-not $unityAvailable) {
        Write-Bad '未找到 unity CLI（PATH）'
    }
    else {
        $outDir = Join-Path $ProjectPath (Split-Path $TestOutput -Parent)
        if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
        Write-Note "执行 unity test --mode EditMode --output $TestOutput"
        & unity test --project-path $ProjectPath --mode EditMode --output $TestOutput 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        if ($LASTEXITCODE -ne 0) { Write-Bad "EditMode 测试未通过（退出码 $LASTEXITCODE），报告见 $TestOutput" }
        else { Write-Ok "EditMode 测试通过，报告见 $TestOutput" }
    }
}

# ── ③ 汇总 ───────────────────────────────────────────────────────────────────
Write-Step '汇总'
# 注意：遍历时不得再调用 Write-Note（它会写 $notes 本身）——用只读遍历
if ($notes.Count -gt 0) { foreach ($n in @($notes)) { Write-Host "  [INFO] $n" -ForegroundColor Yellow } }
if ($failures.Count -eq 0) { Write-Host '  L2 通过 ✅' -ForegroundColor Green; exit 0 }
Write-Host "  L2 失败：$($failures.Count) 项" -ForegroundColor Red
$failures | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
exit 1
