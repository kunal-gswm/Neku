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

## 7. Memory Profile and Footprint

- **Typical Working Set:** ~40 MB – 55 MB (standard .NET 10 WPF runtime baseline).
- **Managed Heap:** ~8 MB – 14 MB.
- **Bitmaps:** Neku's spritesheet (512x1536 PNG) uses ~3 MB decoded uncompressed in GPU/WPF memory cache.
- **Leak Prevention:** Event handlers for menus, timers, and window events are decoupled or weak-referenced; disposable resources implement `IDisposable`.
