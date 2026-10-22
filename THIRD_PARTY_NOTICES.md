# Third-Party Software and Notices

This document lists the third-party software and components incorporated into or adapted for **Neku**, along with their respective licenses and attribution notices.

---

## 1. PixelPaws

- **Project**: PixelPaws
- **Original Author/Contributors**: Copyright (c) 2026 PixelPaws contributors
- **License**: MIT License
- **Original Repository / Base**: PixelPaws desktop pet for Windows

### Notice and License Preservation

The original copyright notice and MIT license for PixelPaws are preserved in the [LICENSE](LICENSE) file in the root of this repository.

### Summary of Modifications for Neku

Neku is a desktop companion application built upon and transforming the PixelPaws codebase. The following key modifications and architectural evolutions have been made to the original codebase:

1. **Rebranding**:
   - Complete project rebranding from PixelPaws to **Neku**.
   - Output assembly renamed to `Neku.exe`.
   - App data and configuration directory relocated to `%AppData%\Neku` (while retaining `DesktopPet` root namespace for internal compatibility).

2. **Target Framework Migration**:
   - Migrated from `.NET 8` (`net8.0-windows`) to `.NET 10` (`net10.0-windows`) to target modern runtime environments and toolchains.

3. **Character Replacement and Art Pipeline**:
   - Replaced default cat sprite art and animations with Neku character sprite assets, manifests, and palette configurations.
   - Preserved modular sheet-loading mechanisms (`AssetSource`) supporting both embedded resources and disk overrides.

4. **AI Companion Removal / Simplification**:
   - Cleaned up external cloud and local LLM integrations (Gemini and Ollama providers, chat input window, speech balloon mechanisms, emotion tag parsers) in favor of lightweight, deterministic, and privacy-first local mechanics.

5. **State Machine & Personality Expansion**:
   - Expanded the internal state machine and behaviour tree.
   - Introduced dedicated personality systems, reactive behaviors, and richer idle/activity cycles beyond the baseline drives.

6. **Developer Event Integration**:
   - Added hooks and reactive triggers tuned for software developers and power users (e.g., build notifications, Git events, IDE focus reactions).

7. **Settings Reorganization**:
   - Restructured settings schema, defaults, and configuration UI to reflect Neku's streamlined feature set, removing deprecated cloud AI configurations and enhancing behavior customization.
