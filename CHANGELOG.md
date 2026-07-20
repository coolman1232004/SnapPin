# Changelog

All notable user-facing changes should be recorded here. Versions follow semantic versioning where practical.

## [1.2.7] - 2026-07-20

### Added

- Quiet startup update notification with a persistent tray action
- Release-details window showing version, edition, download size, notes, and GitHub link
- Downloaded-size, speed, and estimated-time progress
- Separate **Restart and update** / **Later** decision after verification and extraction
- Visible portable updater with replacement progress and post-update confirmation
- Managed portable-package manifest for safe removal of obsolete application files

### Changed

- Verified downloads and staged portable files are reused after choosing **Later**
- Portable replacement is performed by SnapPin's verified staged executable instead of a hidden PowerShell script
- Only the two newest portable rollback copies are retained

### Fixed

- Portable update failures now restore overwritten, deleted, and newly created managed files transactionally
- User-created files beside the portable application are excluded from managed-file cleanup
- Read-only folders trigger elevation before replacement, and insufficient disk space is detected before files change

### Validation

- Successful portable replacement and deliberately failed version validation both covered by real filesystem tests
- Complete x64 release build, localization audit, and smoke-test suite pass with zero warnings or errors

## [1.2.6] - 2026-07-20

### Added

- Cancellable update-download window with percentage, progress status, and a protected preparation phase
- Automated localization-resource, update-dialog, mixed-DPI, portrait-display, remote-session, audio-device, and long-capture tests
- Display/session compatibility details in copied diagnostics

### Changed

- English, Simplified Chinese, and Traditional Chinese text now lives in standard `.resx` resource files
- Capture, annotation, and pinned-image window logic is split into focused partial classes for safer maintenance
- Stale recording-device selections now fall back to the Windows default device

### Fixed

- Remaining rare error and recovery messages are localized
- Tiny or heavily scaled monitor work areas no longer inherit an oversized artificial minimum
- Update downloads can be cancelled safely before installation or portable replacement begins

### Validation

- 481 resource keys aligned across all three interface languages and 425 UI strings audited
- Full x64 release build and smoke-test suite pass with zero warnings or errors

## [1.2.5] - 2026-07-15

### Added

- Application-wide Simplified Chinese and Traditional Chinese interfaces
- Localized tray menus, pin context menus, capture/annotation tools, OCR, recording, history, and update messages
- A localized **Restart now** / **Later** dialog after changing the interface language

### Fixed

- Preferences no longer translates only its tabs while leaving the page content in English
- Traditional Chinese conversion no longer risks an extra character at the end of a window title
- Language changes restart through the single-instance handoff without racing the previous process

### Validation

- Every visible static XAML label is covered by the localization dictionary
- Simplified and Traditional Chinese control tests, visual dashboard/Preferences checks, release build, and full x64 smoke-test suite pass

## [1.2.4] - 2026-07-15

### Added

- True in-place updates for portable SnapPin copies
- Automatic portable-package selection, SHA-256 verification, rollback backup, and restart
- Friendly link to the SnapPin GitHub page in Preferences

### Changed

- Installed copies continue to update through the guided Windows installer
- The internal `release.json` address is no longer displayed or editable
- Existing custom or empty update-source settings migrate to the official GitHub release channel

### Validation

- Installed/portable mode routing and GitHub asset URL regression coverage
- Release build and complete x64 smoke-test suite pass

## [1.2.3] - 2026-07-15

### Fixed

- The capture toolbar now hides while any of the eight selection handles are dragged
- The toolbar reliably returns and re-anchors below the adjusted capture region when resizing ends
- Annotation properties hide and return with the main toolbar instead of floating separately during resizing

### Added

- Official GitHub update channel enabled by default for new and existing installations
- Manual **Check for updates…** command in the system-tray menu
- Clear GitHub update controls in Preferences, alongside automatic startup checks

### Validation

- Resize-toolbar lifecycle, anchoring, GitHub command, and update-feed regression coverage
- Release build and complete x64 smoke-test suite pass

## [1.2.2] - 2026-07-15

### Added

- Clear labelled Pause, Resume, and Stop controls during recording
- Visible Play/Pause control in the recording review panel
- Discard action with confirmation; discarded recordings are not added to history

### Changed

- Recording review now distinguishes Save trimmed, Keep full, and Discard
- Recording output path is shown before the final save decision

### Validation

- Release build and UI regression suite pass on x64 Windows
- Portable and installer SHA-256 values are emitted in `release.json`

## [1.2.1] - 2026-07-15

- Stabilized the pinned-image OCR selection toolbar during scrolling and double-click selection
- Improved toolbar sizing, visual consistency, and icon synchronization
- Expanded local-first capture, history, annotation, OCR, pin, and recording reliability
