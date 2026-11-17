# Silksong Coop · Coop Assist for Hollow Knight: Silksong

English · [简体中文](README.md)

Silksong Coop is a co-op enhancement package for small-group play in *Hollow Knight: Silksong*. SSMP provides the networking; Coop Assist adds progress sharing, teammate help, cocoon spectating, teammate travel and appearance settings on top of the native flow. Target baseline: game **1.0.30000** with **SSMP 0.4.142**.

Current release: **`0.3.1-standard`**, revision **`20261116-review-fixes`**, network id **`0.3.1-std3`**. All players must run the same version and revision. Download from [Releases](../../releases): grab `SSMP-Enhanced-0.3.1-standard.zip` and drop its contents into the game folder — see below.

## Install

1. Game must be version **1.0.30000**. Close the game.
2. Unzip `SSMP-Enhanced-0.3.1-standard.zip` and copy `BepInEx/`, `.doorstop_version`, `doorstop_config.ini` and `winhttp.dll` into the game root, next to `Hollow Knight Silksong.exe`.
3. Start the game. The mod list in the top-left of the main menu should show `Silksong Coop Assist (Standard): 0.3.1`, and SSMP's addon check should show `v0.3.1-std3`.
4. Check that every player's version and revision match before joining.

The archive already contains BepInEx 5.4.23.4, SSMP 0.4.142, ModMenu 0.8.0 with its Chinese localization, DebugMod 1.1.2, I18N, Modlist, UnityHelper, WavLib, the skin loader and the AaXuanYuanTi font. Nothing else to install, and no game files are included. SSMP's per-install security files (`cert.cer`, `key.pem`, `ban`/`whitelist` lists) are generated on first host start and are intentionally not shipped.

## Skins

This release contains **no skins** (the 11 bundled skin sets are ~2.44 GiB, above GitHub's per-file limit, and are not published here). For a different look, **Customizer-style skin packs** work best: place a pack under `Hollow Knight Silksong_Data/Mods/Customizer/<pack name>`. The loader (`BepInEx/plugins/SkinLoader`) ships with the archive and can be used from the main menu or the F7 panel.

## Features

- **Borrowing.** When you cannot afford an item, pressing buy opens a centered dialog: borrow the shortfall from a teammate, pick a lender with **↑ / ↓**, confirm with **Enter** or cancel with **Esc**. The dialog shows the price, your current rosaries and the amount to borrow. The lender sees the same dialog; on approval their rosaries drop and yours rise immediately, the mod finishes the purchase at full price, and the debt is written into the ledger. If the lender declines or is short, you return to the item list with a notice. Hosts can turn borrowing off in **F7** (on by default); pooling the shortfall stays available. Paid benches only ever pool, never borrow.
- **Rosary ledger.** Opens with **F1** while alive (rebindable). Every debt row has a repay button. Loans fall overdue after **10 minutes** of real time and every further **10 minutes** adds another **3 rosaries**; once overdue, loose rosaries are handed back to the lender automatically, and rosaries picked up later repay first. Rosary strings are yours to break.
- **Shared progress, personal resources.** Joining aligns story, wishes, maps and equipment unlocks; wishes, maps and equipment keep syncing while online. Guests who want the host's normal resources can sit at a bench and press **F9**. Personal rosaries, strings, ledger and current crest loadout stay yours.
- **Cocoon and story.** While cocooned, use the game's own quick-map binding to hold the map open. **↑ / ↓** cycles between you and living teammates **in the same scene**. The whole room is lit during the wait (including your own cocoon view) and restored on rescue; cross-scene spectating was removed, and a teammate who leaves the scene returns you to your own view.
- **Team conveniences.** **F5** cocoon-to-bench, **F6** teammate travel, **F8** safe escape, **F10 / F11 / F12** set, use and delete a personal waypoint. **F7** opens the coop panel for hotkeys, skins, HUD and name styles; any keyboard key can be bound as long as it does not collide. Teammate travel and waypoint messages are reported through SSMP's in-game chat.

## Building from source

Requirements:

- Game **1.0.30000**, default path `D:\Game\Hollow Knight Silksong` (use `tools/Build.ps1 -GameRoot` for another location)
- **SSMP 0.4.142** and **ModMenu 0.8.0** installed in that game folder (the build references them; they are not part of this repository)
- .NET SDK 9.0.315 (the scripts use Roslyn's `csc`)
- AaXuanYuanTi font: put it at `assets/Fonts/AaXuanYuanTi-2.ttf`, or have the coop package installed in the game folder (it ships the font). See [assets/Fonts/README.md](assets/Fonts/README.md).

Pipeline: `tools/Build.ps1` → `tools/Test.ps1` → `tools/Audit.ps1` → `tools/Export-Release.ps1`. A full package needs an extracted game template and `tools/Export-Package.ps1 -TemplateRoot <dir>`.

## Repository layout

**Tracked**: `src/` (mod source), `tests/`, `tools/` (build, regression, audit, packaging), `assets/ProgressSyncCatalog.json` (progress catalog embedded in the DLL; the build verifies its hash), `hotreload-docs/`, `skins-docs/`, and the README/brief at the root.

**Local only, git-ignored**: `build/`, `dist/`, `downloads/`, and the vendored third-party snapshots `upstream-*`, `modmenu-*`, `debug-*`, `i18n-source/`, `modlist-source/`. The build scripts fall back to the installed game files when those are missing.

**Release artifacts**: `tools/Export-Release.ps1` produces `dist/SSMP-Enhanced-<version>.zip` (BepInEx plus the doorstop files, with the freshly built DLL and font verified inside) and that is what gets attached to a Release. The update and full packages in `dist/` stay local.

## Development principles

**Quality**: purchases, rescues, story triggers, scene transitions and saves reuse the game's own flow. Charges, refunds, disconnects and reconnects are verified first; persistent state changes check item counts and acquisition flags, and every delivery stays rollback-capable.

**Efficiency**: reusable actions share one entry point; sync sends changes in batches; cross-scene camera frames are gone. Build, regression, native-interface audit and packaging all run through the existing scripts.

**Co-op experience**: teammates should be easy to work with while keeping their own wallet, loadout and habits. Explicit consent, confirmation and in-game feedback tell players what happens next.

## Testing and history

The [iteration brief](Iteration-Brief-0.3.0-standard.md) (Chinese) tracks changes, known limits and the in-game acceptance list. The [validation record](hotreload-docs/Validation-0.3.0-standard.md) separates automated checks from real multiplayer logs. The current revision passes build and regression; borrowing and the cocoon brightness still need individual in-game acceptance.

Tooling: [build](tools/Build.ps1) · [full regression](tools/Test.ps1) · [native interface audit](tools/Audit.ps1) · [release package](tools/Export-Release.ps1) · [update package](tools/Export-Update.ps1) · [full package](tools/Export-Package.ps1).

## License

The Coop Assist source code, tests, tools and documentation in this repository are [MIT licensed](LICENSE). Third-party components bundled in the release archive (SSMP, BepInEx, ModMenu, DebugMod, I18N, Modlist, UnityHelper, WavLib, UnityDoorstop, BouncyCastle, the AaXuanYuanTi font and any skin art) keep their own licenses; versions and sources are listed in [THIRD-PARTY.md](THIRD-PARTY.md).
