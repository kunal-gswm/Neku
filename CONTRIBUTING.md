# Contributing to Neku

Thank you for your interest in contributing to Neku! We welcome contributions that make Neku more reliable, responsive, delightful, and respectful of workstation resources.

Neku is a **native, local-first Windows desktop companion** built with **C# 13, .NET 10, and WPF**.

---

## Code of Conduct & Core Principles

Before opening a pull request, please keep Neku's core tenets in mind:

1. **Native & Lightweight:** Neku runs alongside intensive coding, compiling, rendering, and gaming. Features must never spin fans, consume CPU while idle, or leak memory.
2. **Local-First & Private:** Neku contains zero telemetry, zero analytics beacons, zero cloud accounts, and zero background AI models. All notes, tasks, routines, and custom companions stay strictly on the user's machine.
3. **Non-Intrusive UX:** Windows should never steal focus from the user's active IDE, terminal, or game. All dialogs, HUD pills, and notifications must respect fullscreen apps and presentation mode.
4. **Private Asset Boundary:** Personal companion artwork (`src/DesktopPet/Assets/pets/neku-personal-local/` and local custom packs) must **never** be checked into version control or submitted in pull requests.

---

## Development Setup

### Prerequisites

- **Operating System:** Windows 10 (version 1809 or higher) or Windows 11 (64-bit).
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.100 or higher).
- **IDE / Editor:** Visual Studio 2026, VS Code with the C# Dev Kit extension, or JetBrains Rider.
- **Git for Windows** with PowerShell 7 or Windows PowerShell 5.1.
- **Inno Setup 6** (optional; only needed if compiling the Windows setup installer `.exe`).

### Clone and First Build

```powershell
# 1. Clone the repository
git clone https://github.com/kunal-gswm/Neku.git
cd Neku

# 2. Build the solution (Release configuration)
dotnet build DesktopPet.sln -c Release

# 3. Run the automated test suite
dotnet test DesktopPet.sln -c Release
```

To run Neku directly from your build output:

```powershell
.\src\DesktopPet\bin\Release\net10.0-windows\win-x64\Neku.exe
```

---

## Project Structure

```
├── .github/workflows/          # GitHub Actions CI and release automation
├── docs/                       # Architecture, testing, and release documentation
│   ├── ARCHITECTURE.md         # Component diagrams and state machines
│   ├── CLI.md                  # Terminal command reference and JSON schemas
│   ├── PERFORMANCE.md          # Power, render loop, and memory profiling
│   └── TESTING.md              # Test matrix and coverage guidelines
├── installer/
│   └── Neku.iss                # Inno Setup 6 installer definition
├── scripts/
│   ├── build-installer.ps1     # Compiles Windows Setup installer to artifacts/
│   ├── package-release.ps1     # Release build and packaging pipeline
│   └── publish.ps1             # Self-contained single-file publish script
├── src/
│   ├── DesktopPet/             # Main WPF application (.NET 10)
│   │   ├── Assets/pets/        # Public companion packs (neku, billu, cat variants)
│   │   ├── Engine/             # Physics, state machine, personality, platform collision
│   │   ├── Native/             # Win32 P/Invoke interop (WS_EX_NOACTIVATE, low-level hooks)
│   │   ├── Services/           # Tasks, routines, reminders, notes, settings, focus timer
│   │   └── UI/                 # Pet window, Command Palette, Companion Studio, Settings
│   ├── Neku.Cli/               # Terminal command-line tool (`neku`)
│   └── Neku.Protocol/          # Shared IPC contracts and named-pipe models
├── tests/
│   └── DesktopPet.Tests/       # xUnit test suites (unit, integration, CLI, STA tests)
└── tools/
    ├── benchmark_memory.ps1    # Process memory & CPU verification benchmark
    └── neku-event.ps1          # Helper script for triggering developer webhook events
```

---

## Coding Standards & Conventions

1. **Nullable Reference Types:** All source code enables `#nullable enable`. Do not introduce compiler warnings or ignore null checks.
2. **Win32 & Interop Safety:**
   - Any window created without user focus must include `WS_EX_NOACTIVATE` and `WS_EX_LAYERED` styles.
   - P/Invoke declarations must use strict types (`nint` / `IntPtr`, proper Marshal attributes) and handle window lifecycle teardown cleanly.
3. **Thread Safety & Data Integrity:**
   - Persistence services (`TaskService`, `NotesService`, `SettingsService`, `FocusHistoryService`) serialize disk writes using synchronization locks and atomic `.tmp` moves to avoid data loss during unexpected power loss.
4. **WPF UI Testing:**
   - WPF windows instantiated in unit tests must run inside dedicated `[STAThread]` threads and invoke `Dispatcher.CurrentDispatcher.InvokeShutdown()` upon completion.

---

## Verification Before Submitting

Always run the following checks locally before opening a pull request:

```powershell
# 1. Clean build in Release mode
dotnet build DesktopPet.sln -c Release --nologo

# 2. Run the test suite in Release mode
dotnet test DesktopPet.sln -c Release --nologo

# 3. Verify C# code formatting
dotnet format DesktopPet.sln --verify-no-changes

# 4. Check for whitespace or line-ending anomalies
git diff --check
```

---

## Pull Request Guidelines

1. **Branch Naming:** Create a focused branch from `main`:
   - `fix/brief-description` for bug fixes.
   - `feat/brief-description` for new features or UX enhancements.
   - `docs/brief-description` for documentation and guides.
2. **Focused Scope:** Keep pull requests targeted to one problem or feature. Avoid bundling unrelated formatting or dependency changes.
3. **Include Tests:** Add unit tests for new functionality, bug fixes, or edge cases in `tests/DesktopPet.Tests/`.
4. **No Private Assets:** Check `git status` to guarantee that untracked or private companion assets (`src/DesktopPet/Assets/pets/neku-personal-local/`) are never included.
5. **Describe Verification:** In your PR description, describe the manual and automated steps taken to verify the changes on Windows.

---

## Security Vulnerabilities

Please **do not** use public pull requests or issues to report security vulnerabilities. Refer to our [Security Policy](SECURITY.md) for confidential disclosure instructions.
