# Security policy

## Supported versions

Security fixes are applied to the latest published SnapAnchor release. Older builds may not receive separate patches.

## Reporting a vulnerability

Please do not publish an exploitable security issue, private screenshot, recording, OCR result, configuration file, or diagnostic log in a public issue.

Until a private security contact is configured for the repository, prepare a minimal report containing:

- SnapAnchor and Windows versions
- The affected feature
- Reproduction steps using non-sensitive sample data
- Expected and actual results
- Whether the issue exposes files, clipboard data, screen content, credentials, or code execution

Then use GitHub's private vulnerability reporting feature once it is enabled by the repository owner. If it is not enabled, open a public issue containing no exploit details or private data and ask the maintainer for a private contact method.

## Scope notes

SnapAnchor handles screen pixels, clipboard content, OCR text, recordings, local history, and user-selected links. Treat all of these as potentially sensitive. Current release binaries are not code-signed, so users should download them only from the official repository release page and verify the SHA-256 values supplied in `release.json`.
