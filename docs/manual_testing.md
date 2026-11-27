# Manual Testing & Verification Queue (`manual_testing.md`)

This file is the living ledger for tracking manual quality assurance activities, GUI inspections, hardware/OS-specific verifications, and smoke tests that need to be performed at the keyboard following code changes.

---

## 📖 Protocol & Lifecycle

1. **Agent Queueing Protocol**: Whenever an AI agent or developer implements a feature, bugfix, or refactoring that involves visual UI behavior, filesystem interactions, OS-specific features, or external network integrations, they **must** add an entry under [⏳ Pending Manual Verification](#-pending-manual-verification).
2. **User Keyboard Verification**: When the human tester/maintainer gets to the keyboard:
   - Follow the step-by-step checklist.
   - Check off completed steps (`- [x]`).
   - If issues or deviations are found, record observations under **Tester Notes** and reopen/create a tracking issue.
   - If all steps pass, move the entry to [✅ Completed Verifications](#-completed-verifications).

---

## ⏳ Pending Manual Verification

### [Issue #53]: XAML Bindings, Modal Dialogs, & Update Status
* **Branch / Commit:** `fix/issue-53-gui-bindings-and-update-fallback`
* **Date Added:** 2026-11-26
* **Target Subsystems:** `InkTag.Gui` (`MainWindow.axaml`, `ScraperMatchWindow.axaml`, `App.axaml.cs`, `UpdateService.cs`)
* **Environment / OS:** macOS / Linux / Windows
* **Goal:** Verify `Ctrl+A`/`Cmd+A` selects all rows, `Black & White` and `Bulk Manga` checkboxes toggle cleanly, and `ScraperMatchWindow` displays `MATCH` badge without binding errors.

#### Manual Test Steps:
- [ ] **Step 1 (Verify `Ctrl+A`/`Cmd+A` & Inspector/Bulk Checkboxes):**
  1. Open a folder in InkTag GUI, press `Ctrl+A` (`Cmd+A` on macOS), and verify all rows are selected.
  2. Toggle `Black & White` in the Inspector and `Set Manga Direction` -> `Manga (Right-to-Left)` in Bulk Tools; verify both mark/apply cleanly.

* **Expected Result:** No Avalonia binding errors in debug output and all controls update state as expected.
* **Tester Notes:**

### [Issue #48]: Linux Context Menu Overlay and Blur Box Verification
* **Branch / Commit:** `fix/issue-48-linux-contextmenu-blur-box`
* **Date Added:** 2026-11-22
* **Target Subsystems:** `InkTag.Gui` (`Program.cs`, `App.axaml`, `MainWindow.axaml`, `BulkScrapeQueueWindow.axaml`)
* **Environment / OS:** Linux (X11 / KDE Plasma / XFCE / GNOME) & Cross-platform (macOS, Windows)
* **Goal:** Verify that right-clicking in the DataGrid table opens the context menu cleanly without any oversized dark blurred background box or compositor window shadow artifacts.

#### Manual Test Steps:
- [ ] **Step 1 (Verify Context Menu on Linux):**
  1. Launch InkTag GUI on Linux (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`).
  2. Open a folder containing comics so that the Library Grid populates.
  3. Right-click on any comic row in the DataGrid to open the context menu.
  4. Verify that the context menu appears cleanly directly beneath/at the cursor:
     - The menu items (Auto-Tag, Series Wizard, Bulk Auto-Tag, etc.) are visible and styled properly.
     - There is NO oversized dark blurred box extending behind or around the context menu.
     - No extraneous window-manager drop-shadow rectangles appear.
- [ ] **Step 2 (Verify Context Menu in Bulk Scrape Queue):**
  1. Open the Bulk Auto-Tag Queue window (`BulkScrapeQueueWindow`).
  2. Right-click on an item in the queue table.
  3. Verify that the context menu (Include, Exclude, Tweak Match, etc.) renders cleanly without background blur boxes or clipping.
- [ ] **Step 3 (Verify Non-Linux Regression Testing):**
  1. Run the application on Windows or macOS.
  2. Verify that context menus and flyout popups continue to open and behave normally.

* **Expected Result:** Context menus and popups render sharply without large blurred box artifacts or compositor shadow glitches on Linux.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #47]: Cover Hover Preview Sizing and Show Delay Verification
* **Branch / Commit:** `fix/issue-47-hover-cover-preview`
* **Date Added:** 2026-11-22
* **Target Subsystems:** `InkTag.Gui` (`BulkScrapeQueueWindow.axaml`, `BulkScrapeItemViewModel.cs`, `ScraperMatchWindow.axaml`, `SeriesSearchWizardWindow.axaml`), `InkTag.Core` (`BulkScrapeQueueService.cs`, `PerceptualHashService.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows GUI)
* **Goal:** Verify that hovering over covers in the Bulk Auto-Tag Queue and scraper match windows shows a compact, non-disruptive preview tooltip with the artwork loaded, and that mouse movement over the table does not immediately fire intrusive popovers.

#### Manual Test Steps:
- [ ] **Step 1 (Verify ToolTip ShowDelay Behavior):**
  1. Launch InkTag GUI (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`).
  2. Open a folder with several comic files and launch the Bulk Auto-Tag Queue (Ctrl+B or from the Overview Quick Actions).
  3. Rapidly glide the mouse cursor across the queue table across various columns (Status, Filename, ComicVine Match, Confidence, Covers).
  4. Verify that tooltips DO NOT pop up immediately during mouse transit (400ms delay prevents intrusive popovers).
- [ ] **Step 2 (Verify Compact Local and Remote Cover Tooltips):**
  1. Hover over the `Local Cover` thumbnail column for an item and hold the cursor steady for ~400ms.
  2. Verify the preview tooltip appears:
     - The cover artwork is visible (not blank or "No Cover Extracted" when a valid cover exists).
     - The tooltip image is compactly sized (height ~180px, width ~140px, container max ~180px) and does not dominate or occlude the majority of the window.
     - Filename and image dimensions/format are neatly formatted underneath the cover image.
  3. Once scraper results return, hover over the `Remote Cover` thumbnail:
     - Verify the remote cover tooltip is similarly compact (~180px height).
- [ ] **Step 3 (Verify Scraper Match & Series Search Wizard Hover Previews):**
  1. Open manual Scraper Match Window or Series Search Wizard Window.
  2. Hover over candidate cover images and verify the popover preview is cleanly scaled (~200px max height) with a 400ms delay.

* **Expected Result:** Cover hover tooltips appear smoothly without accidental triggering during cursor movement, load artwork reliably, and maintain a compact footprint that preserves visibility of the queue rows.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #38]: Library Health Dashboard & Navigation Rail Verification
* **Branch / Commit:** `feat/issue-38-library-health-dashboard`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Gui` (`MainWindow.axaml`, `MainWindowViewModel.cs`, `NavRailGroupViewModel.cs`, `ComicItemViewModel.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows GUI)
* **Goal:** Verify that the new Health Dashboard provides high-level diagnostics (tagged %, untagged, errors, last batch job, quick actions) and that the Library Navigation Rail enables one-click filtering by smart lists, series, and publishers while preserving the full 35-column spreadsheet editor.

#### Manual Test Steps:
- [ ] **Step 1 (Verify Empty State & Tab Navigation):**
  1. Launch InkTag GUI (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`).
  2. Verify the application launches into the "📊 Overview" (Dashboard) tab showing the welcome card and an "Open Comic Folder..." button.
  3. Click "📑 Library Grid" in the top tabbar: verify it switches to the spreadsheet DataGrid with the Library Rail on the left and Inspector on the right.
  4. Click "📊 Overview": verify it switches back cleanly.
- [ ] **Step 2 (Verify Health Metric Tiles & Needs Attention):**
  1. Open a folder containing comic files (some tagged, some untagged, some modified, or with errors).
  2. Verify the 4 metric stat tiles display: Tagged (with percentage and progress bar), Untagged, Modified, and Read Errors.
  3. Verify the "Needs Attention" list shows files needing tagging or attention with appropriate status badges (`UNTAGGED`, `ERROR`, `MODIFIED`).
  4. Click the "Inspect →" button on any item in "Needs Attention": verify it switches to the Library Grid with that comic selected and the Inspector panel open.
  5. Click any stat tile (e.g. "Untagged"): verify it switches to the Library Grid with the filter set to Untagged.
- [ ] **Step 3 (Verify Library Rail & Browse Hierarchy):**
  1. In the Library Grid view, inspect the left rail (~220px).
  2. Click "Untagged" in the smart list: verify the grid filters to untagged comics.
  3. In the "Browse" section, switch between "Series" and "Publisher" tabs.
  4. Click a series (e.g. "Spawn"): verify the grid filters to that series and a breadcrumb banner appears above the grid (`Filter: Series: Spawn [✕ Clear]`).
  5. Click `[✕ Clear]`: verify the filter resets and all comics are shown again.
  6. Click the "Rail" toggle button in the toolbar (or View -> Library Rail): verify the rail collapses, and clicking again restores it.
- [ ] **Step 4 (Verify Quick Actions & Batch Rollback on Dashboard):**
  1. On the Overview tab, verify Quick Action buttons: Bulk Auto-Tag, Bulk Rename, and Sync to Komga.
  2. Verify the "Last Batch Job" card displays recent batch information (or "No recent batch jobs recorded" if empty) and provides a "Roll Back Batch" button.

* **Expected Result:** High-level library health diagnostics, fast one-click navigation by publisher/series, and seamless view toggling without spreadsheet regressions.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #21]: Atomic Batch Rollback & Redo Points Verification
* **Branch / Commit:** `feat/issue-21-atomic-batch-rollback`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`MetadataBackupService.cs`, `ArchiveSwapService.cs`, `ComicArchiveHandler.cs`), `InkTag.Mcp` (`ComicTools.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that rolling back a multi-file batch job is truly atomic (staged swaps, pre-flight abort on error, zero partial rollbacks), supports automatic redo point creation, and self-heals interrupted operations via the recovery journal.

#### Manual Test Steps:
- [ ] **Step 1 (Verify Batch Rollback & Redo Point via MCP or CLI):**
  1. Have 2 or 3 test CBZ files tagged in a single batch (e.g. batch ID `batch_manual_test_01`).
  2. Call `RestoreBatchJob("batch_manual_test_01")` via MCP or test code.
  3. Verify that all archives are restored to their pre-batch state and `redoBatchJobId` is returned in the response (e.g. `redo_batch_manual_test_01_<guid>`).
  4. Call `RestoreBatchJob(redoBatchJobId)` using the returned redo ID.
  5. Verify that all archives are reverted back to their edited state.
- [ ] **Step 2 (Verify Pre-Flight Abort Leaves Files Untouched):**
  1. Edit 2 CBZ files under a batch ID `batch_abort_test_02`.
  2. Intentionally delete or rename one of the files from disk.
  3. Call `RestoreBatchJob("batch_abort_test_02")`.
  4. Verify that the operation reports failure (`Failed > 0, Restored == 0`) and the surviving CBZ file remains completely untouched.
- [ ] **Step 3 (Verify Crash Recovery Journal Cleanup):**
  1. Check the InkTag backup directory (`~/.local/share/InkTag/backups/`).
  2. Verify no temporary staging folders (`staging_*`) or `.journal.json` files linger after a normal batch rollback.

* **Expected Result:** Multi-file rollbacks execute all-or-nothing, provide an instant redo option, and cleanly maintain filesystem state without leaving orphan files.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #29]: Nested Directory Preservation & Safe Repackaging Verification
* **Branch / Commit:** `fix/issue-29-archive-repack-structure`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`ArchiveSwapService.cs`, `ComicArchiveHandler.cs`, `ArchiveSecurity.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that modifying metadata or removing pages from a CBZ archive containing nested subdirectories (e.g. `Chapter 1/01.jpg`) preserves directory structure without flattening files to root or losing pages.

#### Manual Test Steps:
- [ ] **Step 1 (Verify Metadata Edit Preserves Folders):**
  1. Create a test CBZ containing subdirectories (e.g. `Chapter 1/01.jpg`, `Chapter 2/01.jpg`).
  2. Run metadata edit via CLI:
     ```bash
     dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- read test_nested.cbz --set-title="Preserved Folders"
     ```
  3. Inspect the contents of `test_nested.cbz` using an archive utility or `unzip -l test_nested.cbz`.
  4. Verify that `Chapter 1/01.jpg` and `Chapter 2/01.jpg` remain in their respective subdirectories and are not flattened to root.
- [ ] **Step 2 (Verify Page Removal in Foldered Archive):**
  1. Run page removal on the foldered archive via CLI:
     ```bash
     dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- remove-page test_nested.cbz 0
     ```
  2. Inspect the archive with `unzip -l test_nested.cbz`.
  3. Verify that only page 0 (`Chapter 1/01.jpg`) was removed, and remaining pages in `Chapter 2/` remain intact in their subdirectories.

* **Expected Result:** Nested directory structures are preserved throughout extraction and repacking cycles, and page removal accurately removes targeted pages across subdirectories.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #20]: Append-Oriented Backup Journal Verification
* **Branch / Commit:** `perf/issue-20-backup-manifest-journal`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`MetadataBackupService.cs`), `InkTag.Mcp` (`ComicTools.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that modifying comic archives appends lightweight records to `backups_journal.jsonl` without constantly rewriting `backups_manifest.json`, and that querying or restoring backups seamlessly reconciles the journal.

#### Manual Test Steps:
- [ ] **Step 1 (Verify Journal Creation on Archive Edit):**
  1. Locate the InkTag backups directory:
     - macOS/Linux: `~/.local/share/InkTag/backups/`
     - Windows: `%LOCALAPPDATA%\InkTag\backups\`
  2. Perform an edit or tag on a comic archive (via CLI `inntag read` or GUI save).
  3. Verify `backups_journal.jsonl` exists in the backups directory and contains a single-line JSON record for the touched comic.
- [ ] **Step 2 (Verify Manifest Compaction on Query):**
  1. List backups using MCP `list_comic_backups` or execute `MetadataBackupService.ListBackups`.
  2. Verify that `backups_journal.jsonl` is removed and its record has been compacted into `backups_manifest.json`.
  3. Verify the newly edited comic backup entry appears at the top of the list with full provenance metadata.

* **Expected Result:** Backup edits append to `backups_journal.jsonl` with zero write amplification, and read queries reconcile the journal into `backups_manifest.json`.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #22]: CLI System.CommandLine Migration Verification
* **Branch / Commit:** `refactor/issue-22-system-commandline`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Cli` (`CliApp.cs`, `Program.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that CLI commands support both `--option value` and standard `--option=value` syntax, while returning identical JSON payloads with `--json`.

#### Manual Test Steps:
- [ ] **Step 1 (Verify `--opt=value` parsing):**
  1. Open terminal and run:
     ```bash
     dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- scan . --missing="Writer,Series" --json
     ```
  2. Verify output is clean, formatted JSON with `"success": true` and `"directory": "."`.
- [ ] **Step 2 (Verify Legacy Direct Path Invocation):**
  1. Create or point to a test comic file (e.g. `test.cbz`).
  2. Run direct path without `read`:
     ```bash
     dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- test.cbz --json
     ```
  3. Verify output returns valid ComicInfo JSON metadata payload without parsing errors.

* **Expected Result:** Both `--option value` and `--option=value` work seamlessly, and JSON outputs match machine-readable contracts.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #33]: Bulk Scrape Memory Optimization & Cover Discard Verification
* **Branch / Commit:** `perf/issue-33-bulk-scrape-release-cover-bytes`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`BulkScrapeQueueService.cs`), `InkTag.Gui` (`BulkScrapeItemViewModel.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that running a bulk scrape releases heavy in-memory cover byte buffers while retaining visual similarity tooltips and thumbnail displays in the GUI.

#### Manual Test Steps:
- [ ] **Step 1 (Bulk Scrape GUI Run):**
  1. Launch InkTag GUI (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`).
  2. Load a folder containing multiple comic archives (.cbz).
  3. Start a Bulk Scrape operation.
  4. Observe the queue items during and after scraping:
     - Verify thumbnail images still render properly for scraped items.
     - Hover over visual match badges (percentage pills) and confirm tooltip text displays expected visual match percentage (not "No Local Cover").
     - Confirm memory usage remains stable and does not climb uncontrollably when queueing 50+ items.

* **Expected Result:** Cover byte buffers are discarded post-hashing to conserve memory, while UI thumbnails and visual match indicators display correctly.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #31]: Atomic State Persistence Verification
* **Branch / Commit:** `fix/issue-31-atomic-file-persistence`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`AtomicFile.cs`, `AppSettings.cs`, `MetadataBackupService.cs`, `ScraperCacheService.cs`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that modifying settings or performing metadata edits writes state via atomic temporary files without leaving leftover `.tmp-*` artifacts, and that corrupted JSON files log warnings instead of silently failing.

#### Manual Test Steps:
- [ ] **Step 1 (Verify Clean Atomic Write):**
  1. Launch InkTag GUI (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`).
  2. Open **Settings**, toggle a setting (e.g. Theme or Debug logging), and click **Save**.
  3. Inspect `~/.config/InkTag/` (or OS config directory):
     - Confirm `settings.json` is updated with valid JSON.
     - Confirm no leftover `.tmp-*` files exist in the directory.
- [ ] **Step 2 (Verify Corrupt JSON Warning):**
  1. Open terminal and write corrupt content to `settings.json`:
     ```bash
     echo "NOT_JSON_DATA" > ~/.config/InkTag/settings.json
     ```
  2. Launch InkTag GUI or CLI.
  3. Inspect logs (`~/.local/share/InkTag/logs/InkTag.log`):
     - Confirm a warning line is present: `[WARN] Failed to read settings from '...'`.
     - Confirm application initializes default settings cleanly without crashing.

* **Expected Result:** Settings and manifests save atomically without temp remnants, and corrupt configurations trigger explicit diagnostic warnings.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #30]: Page-Strip Provenance & Non-Clobbering Backup Verification
* **Branch / Commit:** `fix/issue-30-page-strip-backup-provenance`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`ComicArchiveHandler.cs`, `MetadataBackupService.cs`, Page Stripping)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that stripping pages from an archive creates a listable backup entry in `backups_manifest.json` and does not delete or overwrite pre-existing `.bak` files on disk.

#### Manual Test Steps:
- [ ] **Step 1 (Setup Pre-Existing Backup):**
  1. Have or create a multi-page test CBZ (e.g. `test_comic.cbz`).
  2. Create a dummy file `test_comic.cbz.bak` with known text (e.g. `Precious user backup`).
- [ ] **Step 2 (Execute Page Stripping):**
  1. Strip page index 0 using the CLI or MCP tool:
     ```bash
     dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- strip-intro test_comic.cbz --page 0
     ```
  2. Confirm command completes successfully.
- [ ] **Step 3 (Verify Non-Clobbering and Backup Entry):**
  1. Inspect `test_comic.cbz.bak`:
     Verify file exists and its content is still `Precious user backup` (not overwritten or deleted).
  2. Run list backups tool or inspect `~/.local/share/InkTag/backups/backups_manifest.json` (or OS equivalent):
     Verify an entry exists for `test_comic.cbz` with `OperationType: "RemoveArchivePages"` and change reason describing removed page.

* **Expected Result:** Page removal succeeds, user `.bak` file remains untouched, and metadata backup snapshot is recorded.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #37]: Warn on Unencrypted HTTP Komga Server URLs
* **Branch / Commit:** `fix/issue-37-komga-plaintext-http-warning`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`KomgaClient.cs`, `CleanServerUrl`, `AppLogger`)
* **Environment / OS:** Cross-platform (macOS, Linux, Windows)
* **Goal:** Verify that entering a remote HTTP Komga URL logs a security warning advising the user to use HTTPS, while localhost/loopback connections do not trigger spurious warnings.

#### Manual Test Steps:
- [ ] **Step 1 (Test Remote HTTP Warning):**
  1. Open InkTag GUI (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`) or use the CLI / MCP server.
  2. Open **Settings** and set Komga Server URL to `remote.komga.example:25600` or `http://remote.komga.example:25600`.
  3. Enter an API key and click **Test Connection** (or save).
  4. Inspect the application log (`~/.local/share/InkTag/logs/InkTag.log` on Linux/macOS or via Help $\rightarrow$ Open Logs):
     Verify the log contains:
     `[WARN] [Komga] Inferred unencrypted 'http://' scheme for remote host 'remote.komga.example'` (or `Using unencrypted HTTP...`).
- [ ] **Step 2 (Verify No Warning for Localhost):**
  1. Change Komga Server URL to `http://localhost:25600` or `http://127.0.0.1:25600`.
  2. Test connection or save.
  3. Verify that no plaintext warning is emitted in the log.

* **Expected Result:** Plaintext HTTP warnings are emitted only for non-loopback endpoints to prevent credential leakage.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #32]: Restrict Filesystem Permissions on settings.json (Unix 0600 / 0700)
* **Branch / Commit:** `fix/issue-32-settings-file-permissions`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (`AppSettingsService.cs`, Settings persistence)
* **Environment / OS:** macOS or Linux (Unix permissions)
* **Goal:** Verify that `~/.config/InkTag/settings.json` is saved with `0600` (`-rw-------`) and its directory with `0700` (`drwx------`) to prevent unauthorized local user access to ComicVine and Komga credentials.

#### Manual Test Steps:
- [ ] **Step 1 (Trigger Settings Save):**
  1. Launch InkTag GUI (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`).
  2. Open `Settings` (from menu or toolbar).
  3. Enter or change a setting (e.g. enter dummy ComicVine API key or toggle Theme), then click **Save**.
- [ ] **Step 2 (Inspect Terminal Permissions):**
  1. In terminal, inspect the permissions:
     ```bash
     ls -la ~/.config/InkTag/settings.json
     ls -ld ~/.config/InkTag
     ```
  2. Verify `settings.json` has `-rw-------` permissions (mode `0600`).
  3. Verify `~/.config/InkTag` directory has `drwx------` permissions (mode `0700`).

* **Expected Result:** The configuration directory and file are restricted so that only the current user account has read/write permissions.
* **Tester Notes:**
  <!-- Record manual test observations here -->

---

### [Issue #35]: Windows Reserved Filenames & Trailing Dots Sanitization
* **Branch / Commit:** `fix/issue-35-windows-reserved-filenames` / `6fcaeb3`
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Core` (Renaming Engine) & `InkTag.Gui` (Batch Renamer Preview)
* **Environment / OS:** Windows 10/11 or macOS/Linux interacting with SMB network shares
* **Goal:** Verify that archives renamed with Windows reserved device names (`CON`, `PRN`, `AUX`, `NUL`, etc.) or trailing periods/spaces are safely sanitized and do not trigger OS filesystem errors or access denied crashes.

#### Manual Test Steps:
- [ ] **Step 1 (GUI Batch Renamer Preview):**
  1. Launch InkTag GUI (`dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`).
  2. Open a test folder with 1 or 2 sample `.cbz` files.
  3. Select a comic and edit its metadata:
     - Set `Series` to `"CON"`
     - Set `Title` to `"Special Edition."` (with trailing dot)
  4. Open the Rename Preview modal (`Tools` $\rightarrow$ `Batch Rename Files...`).
  5. Apply template `{Series}`:
     - Verify proposed filename is `_CON.cbz` (not `CON.cbz`).
  6. Apply template `{Series} - {Title}`:
     - Verify proposed filename is `_CON - Special Edition.cbz` (no double dot before `.cbz`).
- [ ] **Step 2 (Physical Rename on Disk):**
  1. Click **Apply Rename** in the preview dialog.
  2. Verify the file on disk successfully renames to `_CON - Special Edition.cbz`.
  3. Verify no exception dialog or crash occurs.
- [ ] **Step 3 (SMB Share Sanity Check - if available):**
  1. Repeat on a folder located on an SMB/CIFS network share.
  2. Confirm network filesystem accepts the renamed archive without I/O errors.

* **Expected Result:** Filenames are safely escaped with `_` prefix for reserved names and trailing periods are trimmed cleanly.
* **Tester Notes:** 
  <!-- Record manual test results or anomalies here -->

---

### [Issue #28]: Windows GUI Bulk Edit Verification on SMB Shares
* **Tracking Issue:** [#28](https://github.com/aurora7795/InkTag/issues/28)
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Gui` (`BulkEditViewModel`, `DataGrid`, `MainWindow`)
* **Environment / OS:** Windows 11 loaded via SMB network share
* **Goal:** Reproduce and verify reported issue #28 where applying values from the Bulk Edit menu does not set values in the GUI for SMB loaded folders.

#### Manual Test Steps:
- [ ] **Step 1 (Load via Network Mount):**
  1. On Windows 11, mount or browse to an SMB share containing `.cbz` / `.cbr` archives.
  2. Click **Open Folder** and select the network folder.
  3. Verify all files populate in the main DataGrid.
- [ ] **Step 2 (Multi-Select & Bulk Edit Sidebar):**
  1. Highlight 3-5 rows in the DataGrid.
  2. Switch to the right-hand inspector tab **Bulk Tools**.
  3. Check the checkbox for **Publisher** and enter `"Image Comics"`.
  4. Check the checkbox for **Genre** and enter `"Sci-Fi"`.
  5. Click **Apply to Selected**.
  6. Observe whether the DataGrid cells update immediately and row styling shows unsaved (`IsDirty`) state.
- [ ] **Step 3 (Save Batch):**
  1. Click **Save All** in the toolbar.
  2. Confirm progress bar advances and archives on SMB share are repacked without permissions failure.

* **Expected Result:** Bulk Edit applies checked fields to all selected items, reflecting in the grid and persisting to disk.
* **Tester Notes:**
  <!-- Record findings here to diagnose #28 -->

---

### [Issue #16]: Velopack In-Place Auto-Updater Check
* **Tracking Issue:** [#16](https://github.com/aurora7795/InkTag/issues/16)
* **Date Added:** 2026-11-19
* **Target Subsystems:** `InkTag.Gui` (`UpdateService.cs`, Velopack integration)
* **Environment / OS:** macOS (.app bundle) or Linux (AppImage)
* **Goal:** Verify behavior when checking for updates and clicking "Install & Restart".

#### Manual Test Steps:
- [ ] **Step 1 (Check for Updates):**
  1. Run the application from an installed bundle (or packaging build).
  2. Click `Help` $\rightarrow$ `Check for Updates...` (or use menu item).
  3. Observe whether an update dialog appears with release notes.
- [ ] **Step 2 (Action on Update):**
  1. If an update is detected, click **Install & Restart Update**.
  2. Note whether Velopack downloads and restarts in-place, or falls back to opening the GitHub release URL in the web browser.

* **Expected Result:** When installed mode is detected, Velopack applies update in-place; otherwise falls back gracefully to browser.
* **Tester Notes:**
  <!-- Record updater behavior here -->

---

## ✅ Completed Verifications

*(No completed manual testing entries yet — move items here as they are verified at the keyboard.)*

---

## 📝 Entry Template for New Issues

When adding a new activity, copy and paste this markdown block under **Pending Manual Verification**:

```markdown
### [Issue #<number>]: <Short Descriptive Title>
* **Branch / Commit:** `<branch-name>` / `<commit-hash>`
* **Date Added:** YYYY-MM-DD
* **Target Subsystems:** `InkTag.Gui` / `InkTag.Cli` / `InkTag.Mcp` / `InkTag.Core`
* **Environment / OS:** Windows / macOS / Linux / SMB
* **Goal:** <One-sentence summary of what this test verifies>

#### Manual Test Steps:
- [ ] **Step 1:** ...
- [ ] **Step 2:** ...
- [ ] **Step 3:** ...

* **Expected Result:** <What should happen when working properly>
* **Tester Notes:** <Observations, logs, or findings>
```
