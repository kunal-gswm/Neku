param(
    [string]$Pack = "neku",
    [int]$IdleSeconds = 300,
    [string]$ExePath = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $distExe  = Join-Path $root "dist\Neku.exe"
    $buildExe = Join-Path $root "src\DesktopPet\bin\Release\net10.0-windows\win-x64\Neku.exe"
    $ExePath  = if (Test-Path $distExe) { $distExe } else { $buildExe }
}

# Ensure settings.json has the requested pack and clean state
$appData = "$env:APPDATA\Neku"
$settingsFile = "$appData\settings.json"
if (Test-Path $settingsFile) {
    $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json
    $settings.activePet = $Pack
    $settings.clickThrough = $false
    $settings.focusTimerActive = $false
    $settings.onboardingDismissed = $true
    $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsFile
}

# Kill any existing Neku
Get-Process -Name Neku -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$proc = Start-Process -FilePath $ExePath -PassThru
$pidNum = $proc.Id

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class NativeMem
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION64
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint AllocationProtect;
        public uint __alignment1;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint __alignment2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION64 lpBuffer, uint dwLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_PRIVATE = 0x20000;
    public const uint MEM_MAPPED = 0x40000;
    public const uint MEM_IMAGE = 0x1000000;

    public static double[] GetCommitted(int pid)
    {
        IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (hProc == IntPtr.Zero) return new double[] { 0, 0, 0 };

        ulong address = 0;
        ulong totalCommitPrivate = 0;
        ulong totalCommitMapped = 0;
        ulong totalCommitImage = 0;
        uint structSize = (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION64));

        while (VirtualQueryEx(hProc, (IntPtr)address, out MEMORY_BASIC_INFORMATION64 memInfo, structSize) != 0)
        {
            if (memInfo.State == MEM_COMMIT)
            {
                if (memInfo.Type == MEM_PRIVATE) totalCommitPrivate += memInfo.RegionSize;
                else if (memInfo.Type == MEM_MAPPED) totalCommitMapped += memInfo.RegionSize;
                else if (memInfo.Type == MEM_IMAGE) totalCommitImage += memInfo.RegionSize;
            }
            address = memInfo.BaseAddress + memInfo.RegionSize;
            if (address <= memInfo.BaseAddress) break;
        }
        CloseHandle(hProc);
        return new double[] { totalCommitPrivate / 1048576.0, totalCommitMapped / 1048576.0, totalCommitImage / 1048576.0 };
    }
}
"@ -ErrorAction SilentlyContinue

$prevCpuMs = 0.0
$phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()

function Get-Snapshot([string]$Phase) {
    $p = Get-Process -Id $pidNum -ErrorAction SilentlyContinue
    if (-not $p) { return $null }

    $currCpuMs = $p.TotalProcessorTime.TotalMilliseconds
    $elapsedMs = $phaseTimer.ElapsedMilliseconds
    $cpuPercent = 0.0
    if ($elapsedMs -gt 500 -and $prevCpuMs -gt 0) {
        $cpuPercent = [math]::Round((($currCpuMs - $prevCpuMs) / ($elapsedMs * [Environment]::ProcessorCount)) * 100, 2)
    }
    $script:prevCpuMs = $currCpuMs
    $script:phaseTimer.Restart()

    # Query GC heap size
    $gcBytes = 0
    try {
        $gcdump = & "$env:USERPROFILE\.dotnet\tools\dotnet-gcdump.exe" report -p $pidNum 2>$null | Select-String "GC Heap bytes"
        if ($gcdump -match '([0-9,]+)\s+GC Heap bytes') {
            $gcBytes = [double]($Matches[1] -replace ',', '')
        }
    } catch {}

    $native = [NativeMem]::GetCommitted($pidNum)

    $snap = [PSCustomObject]@{
        Timestamp = (Get-Date).ToString("HH:mm:ss")
        Phase = $Phase
        PID = $pidNum
        Pack = $Pack
        WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 2)
        PrivateBytesMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
        GCHeapMB = [math]::Round($gcBytes / 1MB, 2)
        CpuPercent = $cpuPercent
        CommitPrivateMB = [math]::Round($native[0], 2)
        CommitMappedMB = [math]::Round($native[1], 2)
        CommitImageMB = [math]::Round($native[2], 2)
    }
    Write-Host "[$($snap.Timestamp)] $($snap.Phase): WorkingSet=$($snap.WorkingSetMB) MB | PrivateBytes=$($snap.PrivateBytesMB) MB | GCHeap=$($snap.GCHeapMB) MB | CPU=$($snap.CpuPercent)%"
    return $snap
}

$results = @()

# 1. Startup snapshot (< 1s)
$results += Get-Snapshot "1. Startup"

# 2. After sprites/character load (5s)
Start-Sleep -Seconds 5
$results += Get-Snapshot "2. Sprites Loaded (5s)"

# 3. 60s
Start-Sleep -Seconds 55
$results += Get-Snapshot "3. Idle 1 min (60s)"

# 4. 180s
Start-Sleep -Seconds 120
$results += Get-Snapshot "4. Idle 3 min (180s)"

# 5. 300s (5 min idle)
Start-Sleep -Seconds 120
$results += Get-Snapshot "5. Idle 5 min (300s)"

$proc | Stop-Process -Force -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize
$results | Export-Clixml "$env:TEMP\neku_mem_baseline_$Pack.xml"
