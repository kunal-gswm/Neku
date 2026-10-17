# Neku Testing and Verification Guide

## 1. Automated Unit Tests

All unit tests are located in `tests/DesktopPet.Tests/` using **xUnit** and the standard `dotnet test` runner under .NET 10.

### Running Unit Tests

Run all unit tests from the repository root:
```powershell
dotnet test d:\Neku\pixelpaws\DesktopPet.sln -c Debug
```

To run with verbose test output:
```powershell
dotnet test d:\Neku\pixelpaws\DesktopPet.sln -c Debug --logger "console;verbosity=detailed"
```

### Test Suite Overview (90 Tests Total)

| Test File | Count | Focus Areas |
| :--- | :--- | :--- |
| **`PersonalityTests.cs`** | 9 | Mood/energy/mischief drift, interaction feedback (petting, booping, dragging), dialogue selection, drive clamping, settings persistence. |
| **`StateMachineTests.cs`** | 12 | State selection weights, autonomous wandering toggles, reduced motion mode, drive limits under sustained activity/rest, duration bounds. |
| **`DeveloperEventsTests.cs`** | 12 | JSON payload deserialization, case insensitivity, default values, supported event types (`build_success`, `test_fail`, etc.). |
| **`DesktopGeometryTests.cs`** | 18 | Screen bounds, monitor intersection, work area constraints, coordinate clamping. |
| **`PlatformsTests.cs`** | 14 | Platform detection, surface boundary collision checks, window edge segmentation. |
| **`QuietModeTests.cs`** | 10 | Full-screen app detection, DND mode detection, presentation mode rules. |
| **`ReplyParsingTests.cs`** | 10 | Speech bubble formatting, markdown cleanup, dialogue token extraction. |
| **`AppVersionTests.cs`** | 5 | Semantic version parsing, build metadata verification. |

**Current Result:** 90 Passed, 0 Failed, 0 Skipped (duration ~50 ms).

---

## 2. Interactive & Manual Verification Checklist

Since Neku is a desktop companion with visual rendering and Win32 interop, the following manual test checklist verifies edge cases:

### A. Window Management & Display
- [x] **Transparency & Borderless:** Verify Neku renders cleanly with no black box, halo, or standard window frame.
- [x] **Dragging:** Left-click and drag Neku to any location. Ensure drag is smooth and bounded.
- [x] **Throwing / Physics:** Release Neku while moving the mouse rapidly; verify momentum is bounded and gravity pulls Neku back down smoothly until landing on the taskbar or desktop work area.
- [x] **DPI Scaling:** Move Neku across displays with different scaling factors (e.g. 100% to 150%); verify sprite scales crisply without pixel distortion.
- [x] **Reset Position:** If Neku is ever lost outside the viewable desktop, right-click the Tray icon and click **Reset Position** (or use Settings > System > Reset Position).

### B. Controls & Interactivity
- [x] **Left-Click (Boop):** Single-click Neku. Neku plays a reaction animation and displays an original dialogue quote.
- [x] **Right-Click Context Menu:** Right-click on Neku. Context menu appears with:
  - Settings...
  - Pause / Resume
  - Hide Neku
  - Click-through Mode
  - Reduced Motion
  - Quiet Mode
  - Reset Position
  - Exit
- [x] **System Tray Menu:** Right-click the cat paw icon in the Windows notification area. Verify all options match and reflect live check states.
- [x] **Click-Through Mode:** Enable Click-Through Mode. Verify clicks pass straight through Neku to underlying applications. Disable Click-Through via the Tray menu.

### C. Developer Event Integration
1. Open **Settings > Developer**.
2. Check **Enable Developer Event Endpoint** (Port 48123). Click **Apply & Close**.
3. In PowerShell, execute:
   ```powershell
   .\tools\neku-event.ps1 build_success "Build passed in 1.4s!"
   ```
   *Expected:* Neku celebrates with a happy jump and speech bubble.
4. Execute:
   ```powershell
   .\tools\neku-event.ps1 test_fail "2 tests failed"
   ```
   *Expected:* Neku displays a surprised/worried reaction.
5. In Command Prompt:
   ```cmd
   tools\neku-event.bat command_complete "Deployment done"
   ```
   *Expected:* Neku hops and confirms completion.

### D. Power Management
- Verify Neku properly freezes rendering when Windows enters Modern Standby or sleep, and resumes ticking upon wake without crashing.
