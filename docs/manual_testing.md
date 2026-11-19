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
