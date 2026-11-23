# FLAFYO — Product Roadmap

## Product direction
FLAFYO is a Windows-first game translation and localization platform. Existing translation and file-analysis features must remain usable as the platform grows.

## Verified baseline (2026-11-23)
- Latest published Windows release at this baseline: **v0.3.4** — [release page](https://github.com/gamingtarek725-cmd/FLAFYO/releases/latest); [direct Windows installer](https://github.com/gamingtarek725-cmd/FLAFYO/releases/latest/download/FLAFYO-GAME-TRANSLATOR-Setup.exe).
- Release workflow `44921025565`: completed successfully, including its test/build/installer/release job.
- Profile import/export security validation workflow `44921125902`: completed successfully.
- Localization catalog refresh workflow `44995357361`: completed successfully.
- Current source includes live OCR/translation, Windows capture and overlay, game-file analysis, localization library, per-game profiles, and offline translation pack workflows.
- Latest documentation update: `7ad003d8ad46cfa944f0f9c902e86adf4a730f5d`.
- Main CI run `45180715934` completed successfully, including unit tests, Windows EXE and installer builds, and light/heavy real-model translation tests.
- CI success is not proof of in-game compatibility. Real-game acceptance testing and performance measurements remain outstanding.

## Delivery phases

### Phase 0 — Foundation and dashboard (implemented; acceptance verification remains)
- Connect every dashboard tile to a real existing screen/action.
- Fix navigation indexes and synchronize dashboard status with the translation engine.
- Keep the dark navy/blue visual system consistent.
- Acceptance: Windows build and tests pass; every tile opens its intended feature.

### Phase 1 — Safe updates and releases (v0.3.4 published; upgrade acceptance remains)
- Publish versioned Windows installers through GitHub Releases.
- Verify installer SHA-256 against the release asset digest before execution.
- Preserve user settings and data across upgrades.
- Acceptance: upgrade an older installation, verify digest, launch, and confirm settings persist.

### Phase 2 — Live translation reliability
- Separate capture from OCR/translation using a bounded latest-frame queue.
- Prevent stale results from appearing after newer frames.
- Preserve click-through overlay behavior and game input.
- Measure OCR, translation, total latency, CPU, memory, and dropped frames.
- Acceptance: repeatable tests in real games with documented measurements.

### Phase 3 — Localization library
- Maintain game metadata, platform, language, version, source team, update date, image attribution, and original source link.
- Add search, filters, details, and update monitoring.
- Do not rehost community translations without permission.
- Acceptance: each visible entry has verified metadata and a working original-source link; missing data is marked.

### Phase 4 — Game file localization engine
- Use read-only inspection by default and create working copies/backups.
- Add format-specific adapters with fixtures and round-trip tests.
- Preserve placeholders, IDs, encoding, and file structure.
- Acceptance: every claimed format has real fixtures and successful extract/edit/rebuild tests.

### Phase 5 — Language and game management (profile import/export implemented)
- Manage offline language packs, disk usage, dictionaries, and per-game profiles.
- Add profile import/export and recovery. (Implemented; automated validation and import/export tests added.)
- Acceptance: settings remain isolated per game and survive upgrades. Import rejects unsafe paths, invalid settings, malformed files, and files larger than 256 KiB; replacement keeps a `.bak` copy.

### Phase 6 — Release hardening (current)
- Clean install/uninstall, upgrade, recovery, logging, accessibility, and documentation.
- Acceptance: clean Windows test machine, regression suite, and release checklist pass.

## Engineering rules
- Persist mutable user data through the shared `Flafyo.Core.AtomicFile` helper: write a same-directory temporary file, then replace the destination; retain `.bak` recovery copies and automatically restore valid settings or game profiles when the primary JSON is damaged.
- Keep persistence behavior covered by unit tests, including backup creation, first-write behavior, cancellation, and temporary-file cleanup.
- Never claim real-game compatibility without a real-game test.
- Never guess archive formats or silently modify original game files.
- Never present placeholder artwork or unverified localization entries as factual.
- Every feature needs a working action, a test, and a clear failure state.
