# Security Policy

## Supported Versions

Neku is an active open-source project. Security updates and bug fixes are provided for the latest published release.

| Version | Supported          | Status |
|:---|:---|:---|
| **1.0.4** | :white_check_mark: Yes | Current stable release |
| < 1.0.4 | :x: No | Unsupported; please update to the latest release |

---

## Reporting a Vulnerability

We take the security of Neku and its users seriously. If you discover a security vulnerability or potential threat in Neku, please report it privately and responsibly.

### Recommended Reporting Channel

Please **do not** open a public GitHub issue, pull request, or public discussion for sensitive security vulnerabilities.

Instead, use **GitHub Private Vulnerability Reporting**:

1. Navigate to the [Neku Security tab](https://github.com/kunal-gswm/Neku/security).
2. Click **Report a vulnerability** to open an advisory draft.
3. Provide:
   - A clear description of the vulnerability and its potential impact.
   - Exact steps or proof-of-concept script to reproduce the issue.
   - Operating system version (e.g., Windows 10 22H2, Windows 11 23H2).
   - Affected Neku binary version and installation method (Setup Installer vs Portable executable).
   - Any suggested mitigations or patches if available.

> **Note for Maintainers & Researchers:** If GitHub Private Vulnerability Reporting has not yet been toggled on by repository administrators, please open a private GitHub security advisory draft at `https://github.com/kunal-gswm/Neku/security/advisories/new` or contact the repository owner ([@kunal-gswm](https://github.com/kunal-gswm)) directly via GitHub. Do not publish vulnerability details until a fix is released.

---

## What to Expect

- **Acknowledgment:** Maintainers review vulnerability reports on a best-effort basis as volunteer availability permits. We do not maintain a 24/7 dedicated security operations center or guaranteed response SLAs.
- **Coordination:** If the report is confirmed, maintainers will work with you in the private security advisory to develop, test, and release a fix.
- **Credit:** Security researchers who responsibly report valid vulnerabilities will be acknowledged in release notes and security advisories (unless they request anonymity).

---

## Scope & Threat Model

Neku is a **local-first Windows desktop companion**. Understanding what is in and out of scope helps ensure effective reporting:

### In Scope
- Named-pipe IPC message tampering, unauthorized command injection, or privilege escalation via local CLI.
- Arbitrary code execution or path traversal in companion pack loading (`manifest.json` parsing) or backup/restore archives.
- Buffer overflows, memory safety violations, or DLL hijacking in native Win32 interop components.
- Unauthenticated remote exploitation of the optional local developer event loopback listener (which must remain strictly bound to `127.0.0.1` and reject non-loopback traffic).

### Out of Scope
- Attacks requiring existing full administrative access (`SYSTEM` or elevated Administrator) or physical control of the host machine.
- Self-inflicted Denial of Service (e.g., intentionally modifying `%AppData%\Neku\settings.json` with invalid JSON or deleting local files).
- Denial of Service caused by exhausting local machine RAM/CPU via third-party tools.
- Vulnerabilities in upstream runtimes or tools (e.g., Microsoft .NET runtime itself or Inno Setup) unless Neku misconfigures them in a way that introduces risk.
- Reports from automated vulnerability scanners without a working proof-of-concept.
