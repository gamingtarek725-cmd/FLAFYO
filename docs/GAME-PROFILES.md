# Game profiles: import and export

FLAFYO stores per-game translation and overlay preferences under the current Windows user's local application data folder. A profile transfer file contains only the settings represented by `GameProfile`; it does not include game files, localization archives, dictionaries, offline language packs, API credentials, or the original game's executable.

## Export a profile

1. Open FLAFYO and locate **إعدادات لكل لعبة**.
2. Select the saved game in **الألعاب المحفوظة**.
3. Choose **تصدير الإعدادات**.
4. Save the `.flafyoprofile` file somewhere safe or transfer it to another PC.

## Import a profile

1. Open FLAFYO on the target PC.
2. Choose **استيراد إعدادات** and select the profile file.
3. If a profile with the same executable name already exists, FLAFYO asks before replacing it.
4. Select the imported game and choose **تحميل** to apply the settings.

When an existing profile is replaced, FLAFYO keeps a `.bak` copy of the previous profile file. Import does not execute the selected file or modify game files.

## File validation

- Maximum import size: 256 KiB.
- The game executable name must be a plain process name; path separators and unsupported characters are rejected.
- Window geometry, capture mode, translation speed, pack selection, and overlay style values are range-checked.
- Invalid JSON, invalid colors, and malformed settings are rejected before saving.
- The profile is written through a temporary file; the previous profile is backed up before replacement.

Only import profile files from sources you trust. Exported profiles are JSON data with a `.flafyoprofile` extension; the extension is not a security guarantee.
