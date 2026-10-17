param(
    [string]$Pack = "neku-personal-local"
)

$ErrorActionPreference = "Stop"

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

$proc = Start-Process -FilePath "d:\Neku\pixelpaws\src\DesktopPet\bin\Release\net10.0-windows\win-x64\Neku.exe" -ArgumentList "--test-interactive" -PassThru
$pidNum = $proc.Id

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class NativeMemInteractive
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

function Get-Snapshot([string]$Phase) {
    $p = Get-Process -Id $pidNum -ErrorAction SilentlyContinue
    if (-not $p) { return $null }

    # Query GC heap size
    $gcBytes = 0
    try {
        $gcdump = & "$env:USERPROFILE\.dotnet\tools\dotnet-gcdump.exe" report -p $pidNum 2>$null | Select-String "GC Heap bytes"
        if ($gcdump -match '([0-9,]+)\s+GC Heap bytes') {
            $gcBytes = [double]($Matches[1] -replace ',', '')
        }
    } catch {}

    $native = [NativeMemInteractive]::GetCommitted($pidNum)

    $snap = [PSCustomObject]@{
        Timestamp = (Get-Date).ToString("HH:mm:ss")
        Phase = $Phase
        PID = $pidNum
        Pack = $Pack
        WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 2)
        PrivateBytesMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
        GCHeapMB = [math]::Round($gcBytes / 1MB, 2)
        CommitPrivateMB = [math]::Round($native[0], 2)
        CommitMappedMB = [math]::Round($native[1], 2)
        CommitImageMB = [math]::Round($native[2], 2)
    }
    Write-Host "[$($snap.Timestamp)] $($snap.Phase): WorkingSet=$($snap.WorkingSetMB) MB | PrivateBytes=$($snap.PrivateBytesMB) MB | GCHeap=$($snap.GCHeapMB) MB"
    return $snap
}

$results = @()

# 1. Startup (< 1s)
$results += Get-Snapshot "1. Startup"

# 2. Focus + HUD + Notes Active (4s)
Start-Sleep -Seconds 4
$results += Get-Snapshot "2. Focus + HUD + Notes Active"

# 3. Closed (at 10s total, 6s after open)
Start-Sleep -Seconds 6
$results += Get-Snapshot "3. After Windows Closed"

# 4. Idle after close (another 10s)
Start-Sleep -Seconds 10
$results += Get-Snapshot "4. Settled After Close (+10s)"

$proc | Stop-Process -Force -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize
$results | Export-Clixml "$env:TEMP\neku_mem_interactive_$Pack.xml"
