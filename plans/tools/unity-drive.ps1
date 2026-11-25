param(
    [string]$Marker = "Library/agent-inspect.pending",
    [string]$ReadyFile = "Logs/agent-inspect.txt",
    [int]$TimeoutSeconds = 180
)

# Аккуратный запуск ожидаемого действия в открытом Unity: маркер + Ctrl+R (Assets → Refresh).
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class UnityFocus {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$root = "F:\work\unity\ragnaGod"
$markerPath = Join-Path $root $Marker
$readyPath = Join-Path $root $ReadyFile

if (Test-Path $readyPath) { Remove-Item $readyPath -Force }
Set-Content -Path $markerPath -Value "1" -Encoding ascii

$unity = Get-Process Unity | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($null -eq $unity) { throw "Unity window not found" }

$hwnd = [IntPtr]$unity.MainWindowHandle
$rect = New-Object UnityFocus+RECT
$null = [UnityFocus]::GetWindowRect($hwnd, [ref]$rect)

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$sent = $false

while ((Get-Date) -lt $deadline) {
    if ([UnityFocus]::GetForegroundWindow() -ne $hwnd) {
        $null = [UnityFocus]::SetForegroundWindow($hwnd)
        Start-Sleep -Milliseconds 600
    }

    if (-not $sent -and [UnityFocus]::GetForegroundWindow() -eq $hwnd) {
        # заголовок окна — безопасное место для клика активации
        $null = [UnityFocus]::SetCursorPos($rect.Left + 400, $rect.Top + 10)
        Start-Sleep -Milliseconds 150
        [UnityFocus]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [UnityFocus]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 400

        # Assets → Refresh (Ctrl+R) — заставляет Unity подхватить новые скрипты
        [UnityFocus]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [UnityFocus]::keybd_event(0x52, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [UnityFocus]::keybd_event(0x52, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [UnityFocus]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
        $sent = $true
        Write-Output "marker written, Ctrl+R sent"
    }

    if (Test-Path $readyPath) {
        Write-Output "READY: $readyPath"
        break
    }

    Start-Sleep -Milliseconds 1200
}

if (Test-Path $readyPath) {
    Write-Output ("size: " + (Get-Item $readyPath).Length)
} else {
    Write-Output "TIMEOUT: результат не появился"
}
Write-Output ("marker still present: " + (Test-Path $markerPath))
