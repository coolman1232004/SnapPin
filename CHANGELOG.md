# Changelog

All notable user-facing changes should be recorded here. Versions follow semantic versioning where practical.

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
