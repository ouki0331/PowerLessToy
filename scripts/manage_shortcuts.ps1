# ==============================================================================
# PowerLessToy - Windows 右下角托盘/系统飞出快捷键管理与防误触工具
# (Windows Shortcut Manager & Mis-touch Prevention Tool)
# ==============================================================================

[CmdletBinding()]
param (
    [ValidateSet("Disable", "Restore", "Status", "Test")]
    [string]$Action = "Disable",

    [string]$TestKey = "K"
)

$kbmDir = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\Keyboard Manager"
$defaultJsonPath = Join-Path $kbmDir "default.json"
$settingsPath = Join-Path $env:LOCALAPPDATA "Microsoft\PowerToys\settings.json"

# 需要拦截的 Windows 右下角托盘/控制面板快捷键清单 (Virtual Key Codes: 91 = VK_LWIN)
$targetShortcuts = @(
    @{ Name = "Win + K (投放与蓝牙外设面板)";    VK = 75; Code = "91;75" },
    @{ Name = "Win + A (快速设置面板: 蓝牙/音量)"; VK = 65; Code = "91;65" },
    @{ Name = "Win + N (通知中心与日历面板)";    VK = 78; Code = "91;78" },
    @{ Name = "Win + P (投影多屏幕设置面板)";    VK = 80; Code = "91;80" },
    @{ Name = "Win + H (语音听写输入悬浮栏)";    VK = 72; Code = "91;72" },
    @{ Name = "Win + W (小组件面板)";            VK = 87; Code = "91;87" },
    @{ Name = "Win + C (Copilot/Teams 面板)";    VK = 67; Code = "91;67" }
)

function Restart-PowerToys {
    Write-Host "`nPowerToys プロセスを再起動して設定を反映します..." -ForegroundColor Cyan
    Stop-Process -Name "PowerToys*", "PowerLessToy*" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $ptExe = Join-Path $env:LOCALAPPDATA "PowerToys\PowerToys.exe"
    if (Test-Path $ptExe) {
        # 尝试通过已配置的交互式计划任务启动，避免 Session 0 隔离
        $task = Get-ScheduledTask -TaskName "StartPowerToys" -ErrorAction SilentlyContinue
        if ($task) {
            Start-ScheduledTask -TaskName "StartPowerToys"
            Write-Host "计划任务 StartPowerToys 启动成功。" -ForegroundColor Green
        } else {
            Start-Process $ptExe
            Write-Host "PowerToys.exe 启动成功。" -ForegroundColor Green
        }
    }
}

if ($Action -eq "Status") {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " 当前快捷键拦截状态 (PowerToys Keyboard Manager)" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    if (Test-Path $defaultJsonPath) {
        $json = Get-Content $defaultJsonPath -Raw -Encoding utf8 | ConvertFrom-Json
        $disabledCodes = $json.remapShortcuts.global | Where-Object { [string]::IsNullOrEmpty($_.newRemapKeys) } | ForEach-Object { $_.originalKeys }

        foreach ($sc in $targetShortcuts) {
            if ($disabledCodes -contains $sc.Code) {
                Write-Host " [已禁用 🚫] $($sc.Name)" -ForegroundColor Yellow
            } else {
                Write-Host " [已启用 🟢] $($sc.Name)" -ForegroundColor Green
            }
        }
    } else {
        Write-Host "尚未生成 Keyboard Manager 规则配置（所有快捷键均为 Windows 原生启用状态）。" -ForegroundColor White
    }
    exit 0
}

if ($Action -eq "Disable") {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " 正在禁用 Windows 右下角托盘/外设弹窗快捷键..." -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    if (!(Test-Path $kbmDir)) {
        New-Item -ItemType Directory -Path $kbmDir -Force | Out-Null
    }

    # 构造禁用映射（newRemapKeys 为空字符串即代表 Disable / 吞噬按键）
    $globalRemaps = @()
    foreach ($sc in $targetShortcuts) {
        $globalRemaps += @{
            originalKeys = $sc.Code
            newRemapKeys = ""
        }
        Write-Host " + 屏蔽规则: $($sc.Name)" -ForegroundColor White
    }

    $kbmConfig = @{
        remapKeys = @{ inProcess = @() }
        remapKeysToText = @{ inProcess = @() }
        remapShortcuts = @{
            global = $globalRemaps
            appSpecific = @()
        }
    }

    $kbmConfig | ConvertTo-Json -Depth 5 | Set-Content $defaultJsonPath -Encoding utf8
    Write-Host "规则已写入: $defaultJsonPath" -ForegroundColor Green

    # 启用 settings.json 中的 Keyboard Manager
    if (Test-Path $settingsPath) {
        $st = Get-Content $settingsPath -Raw -Encoding utf8 | ConvertFrom-Json
        $st.enabled.'Keyboard Manager' = $true
        $st | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding utf8
        Write-Host "PowerToys settings.json 已开启 Keyboard Manager 模块。" -ForegroundColor Green
    }

    Restart-PowerToys

    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host " [完成] 已成功禁用所有右下角弹窗快捷键！" -ForegroundColor Green
    Write-Host " 现在按下 Win+K / Win+A 等快捷键将不会弹出任何窗口。" -ForegroundColor White
    Write-Host " 如需打开这些功能，请直接使用鼠标点击桌面右下角对应的托盘图标。" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Green
    exit 0
}

if ($Action -eq "Restore") {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " 正在恢复所有 Windows 原生快捷键..." -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    if (Test-Path $defaultJsonPath) {
        $emptyConfig = @{
            remapKeys = @{ inProcess = @() }
            remapKeysToText = @{ inProcess = @() }
            remapShortcuts = @{
                global = @()
                appSpecific = @()
            }
        }
        $emptyConfig | ConvertTo-Json -Depth 5 | Set-Content $defaultJsonPath -Encoding utf8
        Write-Host "已清空快捷键屏蔽规则。" -ForegroundColor Green
    }

    Restart-PowerToys
    Write-Host "`n已恢复 Windows 原生快捷键！" -ForegroundColor Green
    exit 0
}

if ($Action -eq "Test") {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host " 快捷键模拟键入测试" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    $matched = $targetShortcuts | Where-Object { $_.Name -like "*$TestKey*" }
    if (!$matched) {
        Write-Host "未找到对应快捷键: $TestKey" -ForegroundColor Red
        exit 1
    }

    Write-Host "将在 2 秒后模拟按下: $($matched.Name)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2

    # 加载底层键盘模拟程序
    $signature = '[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);'
    if (-not ([System.Management.Automation.PSTypeName]'KeyboardSimulator.WinKeyNative').Type) {
        Add-Type -MemberDefinition $signature -Name "WinKeyNative" -Namespace "KeyboardSimulator"
    }

    $vk = [byte]$matched.VK
    [KeyboardSimulator.WinKeyNative]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero) # Win down
    Start-Sleep -Milliseconds 50
    [KeyboardSimulator.WinKeyNative]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)  # Key down
    Start-Sleep -Milliseconds 50
    [KeyboardSimulator.WinKeyNative]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)  # Key up
    Start-Sleep -Milliseconds 50
    [KeyboardSimulator.WinKeyNative]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero) # Win up

    Write-Host "模拟键入已发送！如果已禁用，则桌面右下角不会有任何反应。" -ForegroundColor Green
    exit 0
}
