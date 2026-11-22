# FLAFYO Cyber redesign

## Approved product direction

- Visual identity: Cyber Gaming, using graphite surfaces, electric violet, cyan highlights and restrained glow.
- Primary language: Arabic, with right-to-left layout.
- Localization library: a first-class in-app storefront, not a secondary link.
- Platform: Windows desktop (WPF); retain existing translation, OCR, overlay, profile and file-localization services.

## Design system

The shared WPF resource dictionary in `src/Flafyo.App/App.xaml` defines the canonical palette, cards, typography, buttons, inputs, tabs, list selection, progress bars and focus states. Feature windows should consume these shared resources rather than introducing unrelated local palettes.

## Application information architecture

1. Home — engine state, quick actions, recent game and installed packs.
2. Live translation — capture mode, OCR region, start/stop and latency.
3. Localization Store — searchable game cards, platform/category filters, entry details, compatibility notes and original-source navigation.
4. Translation packs — OPUS, NLLB and optional MADLAD, with install state, disk use and resource warnings.
5. Game profiles — per-game settings, import/export and recovery.
6. Game file localization — read-only scan, extraction, translation, separate output and backup-aware apply/restore.
7. Glossaries — game terminology dictionaries and custom terms.
8. Translation history — source text, Arabic correction and saved corrections.
9. Overlay appearance — live preview, typography, placement, opacity and original-text visibility.
10. Performance and diagnostics — OCR/translation timing, CPU/memory observations and logs.
11. Settings and updates — language, shortcuts, startup behavior, update status and about information.

## Delivery and acceptance

- Preserve every existing named WPF control and event handler while migrating layouts.
- Do not replace or rewrite the translation engine as part of a visual-only change.
- Every visible action must map to an existing or explicitly implemented command.
- Store entries must disclose source, platform, version compatibility and missing metadata; link to original publishers and do not redistribute community files without permission.
- Verify RTL, keyboard focus, resizing, high-DPI behavior and readable contrast.
- Run unit tests and Windows publish/installer workflows after each migration batch.
- Treat MADLAD as experimental until its exact model artifacts, tokenizer, ONNX graph inputs and real Arabic translation test all pass.

## Current implementation slice

This branch establishes the shared Cyber theme, updates the main dashboard's visual treatment and gives the existing localization storefront more space and consistent cyber accents. The remaining feature-window layout migrations must be completed and verified in subsequent commits before calling the redesign complete.
