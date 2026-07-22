# Contributing to SnapAnchor

Thank you for helping improve SnapAnchor.

## Bug reports

Before opening an issue:

1. Test the latest release.
2. Check whether the issue has already been reported.
3. Reproduce it using non-sensitive sample content.
4. Record the SnapAnchor version, Windows version, display scaling, monitor layout, and exact steps.

Do not attach private screenshots, recordings, clipboard data, OCR results, configuration, history, session data, or logs without reviewing and redacting them.

## Feature requests

Explain the problem or workflow first, then the proposed behaviour. Screenshots from other products may help explain a public interaction, but do not submit their proprietary code, icons, artwork, branding, or packaged assets.

## Code contributions

SnapAnchor is released under the MIT License. By submitting a contribution, you agree that it may be distributed under the same license.

Code contributions are expected to:

- Keep capture, OCR, history, and recording local-first by default
- Preserve mixed-DPI and multi-monitor correctness
- Avoid unnecessary additional windows during capture and annotation
- Include focused regression coverage
- Pass the x64 build and smoke suite
- Retain third-party license notices
- Avoid unrelated formatting or generated build output

## Development checks

```powershell
dotnet build .\SnapAnchor.csproj -c Release -p:Platform=x64
.\Tests\Run-Smoke.ps1
```
