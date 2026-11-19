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
