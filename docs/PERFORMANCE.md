# Neku Performance and Power Consumption Guide

## 1. Overview and Core Philosophy

Neku is designed from the ground up as a **zero-waste Windows desktop companion**. A desktop pet should be fun and endearing, never a resource hog that spins the user's laptop fans, degrades gaming frame rates, or drains battery life during presentations or travel.

### Key Performance Pillars:
1. **Zero-Cost Inactivity:** When Neku is paused, hidden, or in quiet mode, `CompositionTarget.Rendering` is detached entirely. Rendering CPU usage drops to **0.0%**.
2. **Configurable Frame Rate:** Retro pixel art looks best and runs leanest at 12–15 FPS. Neku defaults to 14 FPS, avoiding unnecessary 60/120/144 Hz rendering cycles.
3. **Smart Surface Enumeration:** Desktop window edge scanning is throttled: every 3 seconds when stationary, every 0.25 seconds when walking or falling. Neku never spams `EnumWindows` on every frame.
4. **Cached Visual Assets:** Spritesheets are loaded and decoded once into memory. No per-frame bitmap allocations or garbage collection pressure.
5. **No Background AI or Telemetry:** Deterministic local rules govern behavior and dialogue. Zero network calls on startup, zero telemetry, zero background language model inference.

---

## 2. Rendering Lifecycle and Dispatcher Optimization

### A. Dynamic Render Loop Detachment
WPF applications using `CompositionTarget.Rendering` normally receive callbacks on every display refresh (e.g. 60–144 times per second), even if the UI element hasn't visually changed.

In Neku:
- **`PetWindow.SetRenderLoopEnabled(bool enabled)`** subscribes to or detaches from `CompositionTarget.Rendering` dynamically.
- When the pet is **paused**, **hidden by the user**, or **hidden by Quiet Mode** (e.g., during full-screen games or PowerPoint presentations), the render handler is completely disconnected.
- The decorations (sparkles, hearts, pebbles, speech bubbles) are cleaned up before detachment, preventing frozen visual artifacts.

### B. Frame Rate Throttling
- The animation loop measures elapsed time between ticks (`Stopwatch`).
- Frames are only advanced when the elapsed time exceeds `1.0 / TargetFps` (configurable in Settings > Appearance / Performance; default: 14 FPS).
- During idle states (Sitting, Loafing, Sleeping), sprite frames change even less frequently, using one-shot state durations rather than infinite rapid loops.

---

## 3. Desktop Surface & Window-Edge Detection

Neku can walk along the top edges of open application windows and land on them. Continuously enumerating all top-level windows via Win32 `EnumWindows` would consume measurable CPU.

### Optimization Strategy:
- **Stationary State:** When sitting, sleeping, or idle, window edges are refreshed at most once every **3.0 seconds**.
- **Active Movement:** When walking, falling, or dragged, edges are refreshed at most once every **0.25 seconds**.
- **Filtering:** Only valid, unminimized, visible application windows with non-empty titles and normal window styles are evaluated.
- **Cache Reuse:** The resulting line segment list is cached and queried using binary spatial checks.

---

## 4. Background Services and Threading

| Component | Default State | Execution Model | Resource Impact |
| :--- | :--- | :--- | :--- |
| **Animation Loop** | Active (14 FPS) | UI thread (`CompositionTarget.Rendering`) | ~0.1% – 0.5% CPU when moving |
| **Input Watchdog** | Active | Low-priority Dispatcher timer (every 10s) | Inactive except on system lock |
| **Quiet Watcher** | Active | Low-priority Dispatcher timer (every 2s) | Instantaneous check of fullscreen/DND flags |
| **State Checkpoint** | Active | Dispatcher timer (every 30s) | Writes single small JSON file (< 2 KB) only if changed |
| **Developer Events** | **Disabled** | None (0 threads, 0 sockets) | 0.0% CPU, 0 MB RAM |
| **Developer Events (Enabled)** | Opt-in | Asynchronous `HttpListener` on loopback | Event-driven, 0% CPU while idle |
| **AI Companion** | **Disabled** | None | 0.0% CPU, 0 MB RAM |

---

## 5. System Power & Session Transitions

Neku listens for Windows system power events via `Microsoft.Win32.SystemEvents.PowerModeChanged`:
- **`PowerModes.Suspend`:**
  - Suspends animation loop.
  - Pauses all timers.
  - Drops hooks.
- **`PowerModes.Resume`:**
  - Resumes engine state.
  - Refreshes desktop work area and monitor bounds.
  - Neku wakes up gracefully without crashing or losing position.

---

## 6. Battery and Performance Modes

Users can toggle performance settings via the Settings window or Tray menu:
- **Quiet Mode (`EnableQuietMode`):** Automatically hides Neku during full-screen games, video playback, and presentations.
- **Battery-Conscious Mode (`BatteryConscious`):** Detects battery operation and drops movement speed, scales back high-energy states (Zoomies), and increases rest intervals.
- **Reduced Motion (`ReducedMotion`):** Completely disables screen-wide sprints (Zoomies), cursor chasing, and high leaps, keeping Neku calm and stationary.

---

## 7. Empirical Memory & Power Profile (October 2026 Audit)

### A. Test Environment & Measurement Methodology
- **Audit Date:** October 17, 2026
- **Operating System:** Windows 11 x64 (Build 26100)
- **Hardware Profile:** Intel Core i7, Dedicated NVIDIA GPU (GeForce RTX series) + Intel UHD display pipeline (1920x1080 resolution, 100% DPI scaling).
- **Application Target:** Neku Release artifact (`dist\Neku.exe`, self-contained single-file binary, 156.9 MB).
- **Diagnostics Methodology:**
  - **Working Set (`WorkingSet64`):** Total physical RAM pages mapped into the process address space (including executable code, shared runtime assemblies, and GPU driver DLLs). Measured via PowerShell `Get-Process`.
  - **Private Committed Bytes (`PrivateMemorySize64` & Win32 `VirtualQueryEx` `MEM_PRIVATE`):** Memory exclusively reserved and committed for this process (heap, stacks, and process-private memory buffers).
  - **Managed GC Heap:** Live managed object allocations tracked by the .NET 10 Garbage Collector, sampled using `dotnet-gcdump report -p <pid>`.
  - **CPU Utilization (%):** Total processor time delta (`TotalProcessorTime`) normalized across all logical processors over the measurement interval.

### B. 5-Minute Idle Timelines (Published Release Binary)

The table below reflects empirical measurements taken across 5 minutes of continuous idle execution using [`tools/benchmark_memory.ps1`](../tools/benchmark_memory.ps1):

#### Generic Companion (`neku`)
| Phase | Elapsed | Working Set | Private Bytes | GC Heap | CPU % |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Startup** | < 1s | 114.73 MB | 48.57 MB | 2.13 MB | 0.00% |
| **Sprites Loaded** | 5s | 122.57 MB | 54.86 MB | 2.07 MB | 0.68% |
| **Idle 1 min** | 60s | 131.23 MB | 62.21 MB | 2.06 MB | 0.16% |
| **Idle 3 min** | 180s | 126.28 MB | 56.84 MB | 2.02 MB | 0.24% |
| **Idle 5 min** | 300s | 127.19 MB | 57.17 MB | 2.02 MB | 0.23% |

#### Personalized Local Companion (`neku-personal-local`)
| Phase | Elapsed | Working Set | Private Bytes | GC Heap | CPU % |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Startup** | < 1s | 116.30 MB | 49.96 MB | 2.68 MB | 0.00% |
| **Sprites Loaded** | 5s | 123.73 MB | 56.20 MB | 2.61 MB | 0.93% |
| **Idle 1 min** | 60s | 130.23 MB | 61.15 MB | 2.61 MB | 0.25% |
| **Idle 3 min** | 180s | 127.59 MB | 58.12 MB | 2.47 MB | 0.16% |
| **Idle 5 min** | 300s | 129.22 MB | 59.55 MB | 2.47 MB | 0.14% |

*(Note: Prior documentation estimates of 40–55 MB reflected framework-dependent baseline runs or basic virtualized environments where GPU driver binaries were unmapped; the figures above represent actual Windows 11 desktop execution on dedicated hardware).*

### C. Interactive Companion Window Lifecycle

Opening secondary interactive windows (Focus HUD, Quick Notes, Developer Events, Notifications) temporarily creates WPF visual trees, DirectX swap chains, and window handles (`HwndSource`).

Measured via [`tools/measure_interactive.ps1`](../tools/measure_interactive.ps1):
| Lifecycle Phase | Working Set | Private Bytes | GC Heap |
| :--- | :--- | :--- | :--- |
| **1. Startup (Idle Pet Window)** | 112.45 MB | 47.40 MB | 2.18 MB |
| **2. Active Focus Session + HUD + Quick Notes** | 173.58 MB | 95.73 MB | 3.35 MB |
| **3. Immediately After Closing Secondary Windows** | 165.89 MB | 88.02 MB | 3.32 MB |
| **4. Settled Post-Close (+10s)** | 164.31 MB | 87.13 MB | 3.15 MB |

**Resource Cleanup Safeguards:**
- Window references in `App.xaml.cs` (`_notesWindow`, `_devEventsWindow`) are explicitly nullified on the `Closed` event to prevent retained references in the application shell.
- Notification auto-close timers (`_autoCloseTimer`) and HUD update timers (`_displayTimer`) are stopped on `Closed` and `IsVisibleChanged` to eliminate background Dispatcher activity when hidden.
- The `EffectsOverlay` full-screen transparent canvas remains hidden (`Hide()`) when no active particles or dialogue bubbles are on screen, avoiding idle DWM composition overhead.

### D. Hardware Acceleration vs. Software Rendering Pipeline

- **Hardware Acceleration (Default):**
  - Uses Direct3D 9Ex acceleration and hardware composition swap chains.
  - Smooth 14 FPS animation with minimal CPU rasterization overhead.
  - Trade-off: Dedicated GPU drivers load shared driver binaries into the process working set (`nvgpucomp64.dll` ~45.1 MB, `nvd3dumx.dll` ~29.6 MB).
- **Software Rendering Mode (`--software-rendering` or `UseSoftwareRendering` Setting):**
  - Forces `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly`.
  - Bypasses Direct3D hardware initialization, completely preventing GPU driver DLLs from mapping into process RAM (reducing Working Set to ~100 MB and Private Bytes to ~43 MB).
  - Trade-off: All sprite blitting, transparency masks, and UI scaling execute on the CPU via `wpfgfx_cor3.dll`. Recommended for virtual machines, low-RAM laptops, or battery-constrained environments.

---

## 8. Limitations & Real-World Caveats

1. **No Universal Working Set Guarantee:**
   Memory numbers on Windows are not static constants. Working set is heavily dependent on:
   - GPU manufacturer and driver version (e.g. NVIDIA driver mappings add ~75 MB, whereas Intel/AMD drivers map different footprints).
   - Display count, desktop resolution, and Per-Monitor DPI scaling (higher resolutions require larger DWM composition surfaces).
   - System memory pressure: Windows aggressively trims process working sets when physical RAM is constrained.
2. **Memory Reclamation Dynamics:**
   When secondary windows (Notes, Settings, Focus HUD) are closed, WPF detaches their visual trees. However, memory pages are not immediately returned to the OS until the .NET runtime triggers a Gen 2 garbage collection or the Windows memory manager trims unused pages.
3. **Realistic CPU Expectations:**
   While idle stationary states consume negligible CPU (~0.0% to 0.2% on periodic watchdog ticks), active states (walking across window tops, running zoomies, tumbling physics, spawning particle bursts) consume measurable CPU (~0.5% to 1.5% depending on hardware clock speeds). Neku is optimized for zero-cost inactivity, but does not claim absolute zero CPU usage under active interaction.
