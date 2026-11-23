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

## FLAFYO development execution checklist (updated 2026-11-23)

This is the single ordered checklist for future development. A checkbox is marked complete only after the stated acceptance evidence exists. CI success does not substitute for testing on the target PC or inside real games.

### Track A — MADLAD-400 3B INT4 (priority 1)
- [x] Pin the MADLAD ONNX export revision and record exact file sizes and SHA-256 hashes in `LanguagePack.Madlad`.
- [x] Add a dedicated integration test that loads the full encoder/decoder pack and translates three English game/UI sentences to Arabic.
- [x] Add a dedicated CI workflow that downloads the pinned model files and runs the real translation test on CPU.
- [x] Correct the Arabic Unicode-range assertion in the integration test.
- [ ] Confirm the CI model download completes with all seven files matching their pinned sizes/hashes.
- [ ] Confirm ONNX Runtime successfully opens both encoder and merged decoder graphs, including external weight sidecars.
- [ ] Confirm all three outputs are non-empty Arabic and manually review translation quality.
- [ ] Measure cold-load time, first-token latency, total sentence latency, peak RAM, and CPU usage.
- [ ] Test GPU execution on the target RTX 3050 8 GB; retain CPU fallback if provider/runtime compatibility is unavailable.
- [ ] Verify cancellation, repeated translations, disposal, and no memory growth across a sustained test.
- [ ] Integrate the model into the in-app pack selector only after the preceding acceptance checks pass; show disk/RAM requirements and a clear low-memory warning.
- [ ] Verify interrupted downloads resume safely or restart cleanly, hash failures are rejected, and uninstalling a pack does not remove user data.

### Track B — Windows application reliability (priority 2)
- [ ] Run the full unit/integration test suite and fix all regressions.
- [ ] Verify every sidebar/dashboard action opens the correct screen and reports failures clearly.
- [ ] Verify OCR region capture, change detection, bounded queue behavior, and stale-frame rejection.
- [ ] Verify overlay click-through and game keyboard/mouse/controller input are unaffected.
- [ ] Test at least three real games with different rendering modes; record OCR, translation, and end-to-end latency.
- [ ] Verify Arabic shaping, punctuation, line wrapping, mixed RTL/LTR text, and game-specific terminology.
- [ ] Test language pack install/update/remove, cache, history, profiles, and glossary persistence.
- [ ] Test clean install, upgrade from the previous release, uninstall, and settings preservation on Windows 10/11.
- [ ] Verify EXE icon, installer branding, version metadata, signing status, and SHA-256 artifact.
- [ ] Publish a release only after the Windows build, tests, installer, and manual acceptance checklist pass.

### Track C — Game-file localization (priority 3)
- [ ] Inventory supported formats and document read-only inspection behavior.
- [ ] Add real fixture files and extract/edit/rebuild round-trip tests per supported format.
- [ ] Preserve encoding, IDs, placeholders, archive layout, and original files; always work on copies with backups.
- [ ] Validate import/export and reject malformed or unsafe paths.
- [ ] Document unsupported formats instead of implying universal compatibility.

### Track D — Android app (separate deliverable, priority 4)
- [ ] Connect and inspect the actual Android project; do not treat the Windows WPF project as an APK source.
- [ ] Confirm native Android architecture, minimum/target SDK, package ID, and supported OS versions.
- [ ] Implement screen capture/OCR with Android-supported permissions and foreground-service behavior.
- [ ] Implement a floating overlay that respects user control and Android restrictions.
- [ ] Integrate downloadable offline translation packs with storage and memory controls.
- [ ] Build a signed test APK and verify install/upgrade on physical Android devices.
- [ ] Test game input, capture reliability, battery, thermals, and latency on low-memory devices.

### Track E — Documentation and release (priority 5)
- [ ] Keep setup, model requirements, known limitations, troubleshooting, privacy, and license notices current.
- [ ] Include reproducible test evidence and benchmark hardware/software details.
- [ ] Publish installer/APK checksums and release notes.
- [ ] Maintain this checklist by changing boxes only when acceptance evidence is available.

### Current gate
**MADLAD is not yet accepted as working.** The real-model CI run must finish its download and execute the translation test before it can be marked operational. The test currently runs on CPU; GPU acceleration and target-device performance remain separate acceptance items.
