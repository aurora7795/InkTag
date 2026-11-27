# Agent Rules & Guidelines (AGENTS.md)

## Git Branching Rules
- **Never modify code directly on `main`**: Before executing code edits or file modifications, always check the current branch. If the current branch is `main`, automatically create and switch to a descriptive branch (e.g., `feat/feature-name` or `fix/issue-description`) before making changes.
- **Exceptions**: Only remain on the current branch if the work is explicitly a continuation or follow-up related to the current non-main branch.

## Task & Issue Tracking Rules
GitHub Issues serve as the single source of truth and central backlog for all pending, deferred, and ongoing work across the repository.

### 1. Mandatory Issue Capture for Deferred Work
- **Zero Forgotten Work**: Never leave deferred work, newly discovered bugs, architectural flaws, technical debt, or out-of-scope feature ideas as ephemeral conversation comments or hidden in-code `TODO`s.
- **Immediate Filing**: Whenever an issue, bug, refactoring need, or feature cannot or should not be addressed within the immediate task scope, proactively create a GitHub Issue:
  ```bash
  gh issue create --title "<type>(<scope>): <clear title>" \
    --label "<labels>" \
    --body "<structured description>"
  ```
- **Issue Types & Title Prefixes**:
  - `fix(<scope>): ...` — Bugs, unintended behaviors, or regressions.
  - `feat(<scope>): ...` — New functionality, CLI flags, or GUI controls.
  - `perf(<scope>): ...` — Memory leaks, socket reuse, disk thrashing, or algorithmic improvements.
  - `refactor(<scope>): ...` — Code cleanliness, modularity, or deprecation cleanup.
  - `tech-debt(<scope>): ...` — Architectural shortcuts, missing error handling, test coverage gaps.
  - `docs(<scope>): ...` — Wiki, architecture diagrams, or developer guides.

### 2. Standard Issue Structure
When creating issues via `gh issue create`, provide a clear, actionable body:
- **Problem / Context**: What is the current behavior, deficiency, or missed requirement?
- **Proposed Solution / Direction**: Suggested implementation path, architectural guidance, or alternatives.
- **Affected Files / Modules**: Specific file paths, classes, or CLI/MCP/GUI components involved.
- **Acceptance Criteria**: Concrete checklist of what done looks like.
- **Origin / References**: Reference related issues, commits, review findings, or conversations.

### 3. Triage & Labeling Standards
Apply appropriate labels when creating or triaging issues:
- `bug` — Broken functionality or crash.
- `enhancement` — New feature or capability.
- `deferred` — Deliberately captured for future implementation.
- `tech-debt` — Technical debt, refactoring, or architectural hygiene.
- `documentation` — Wiki, docs, or README updates.

### 4. Issue-Driven Development Workflow
- **External Bug Priority (Top Priority)**: Any bug raised by external users or contributors (anyone other than the repository owner/maintainers or AI agents) is **top priority** and must be triaged and addressed ahead of internal backlog, refactoring, or feature work. Always check issue authors when reviewing `gh issue list`.
- **Backlog First**: Before planning or starting significant work, review active issues (`gh issue list`) to avoid duplicate work and leverage existing context.
- **Active Issue Context**: When starting work on an issue:
  1. Inspect the issue: `gh issue view <issue-number>`
  2. Create a dedicated branch linked to the issue (e.g., `fix/issue-20-backup-manifest`).
- **Commit & PR Resolution**:
  - Reference the issue in commit messages: `Fixes #<issue-number>: <description>` or `Closes #<issue-number>: <description>`.
  - When cutting releases or logging changes in `CHANGELOG.md`, reference the issue number.
  - Ensure the issue is closed upon merging or resolving.

## Manual Verification Queue Rules (manual_testing.md)
- Maintaining [`manual_testing.md`](../manual_testing.md) is a **MANDATORY requirement** whenever changes involve UI/UX behavior, filesystem operations, external APIs, OS-specific behavior, or batch processing.
- **Queueing Verification Steps**: Before concluding an issue or work session, the agent must append a structured entry under `## ⏳ Pending Manual Verification` in `manual_testing.md` with explicit, step-by-step test instructions and expected results for the user to execute at the keyboard.
- **User Resolution**: The user checks off items (`- [x]`) upon hands-on verification and moves completed blocks to `## ✅ Completed Verifications`.

## Changelog Maintenance Rules
- Maintaining [CHANGELOG.md](../CHANGELOG.md) is a **MANDATORY requirement**.
- Every feature, bugfix, or release must update `CHANGELOG.md` following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) standards (`Added`, `Changed`, `Fixed`, `Security`).
- When cutting a release, migrate `[Unreleased]` into the new version section and retain an empty `[Unreleased]` block.
