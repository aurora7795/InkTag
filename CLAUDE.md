# Project Constitution & AI Guide (CLAUDE.md)

This file sets the build, test, and style guidelines for the InkTag repository, and establishes the schema for maintaining the Layer 2 LLM Wiki.

---

## 1. Commands & Workflows

### 1.1 Compile & Build
* **Whole Solution**: `dotnet build InkTag.slnx`
* **Core Library**: `dotnet build src/InkTag.Core/InkTag.Core.csproj`
* **CLI Application**: `dotnet build src/InkTag.Cli/InkTag.Cli.csproj`
* **MCP Server**: `dotnet build src/InkTag.Mcp/InkTag.Mcp.csproj`
* **GUI Application**: `dotnet build src/InkTag.Gui/InkTag.Gui.csproj`
* **Test Suite**: `dotnet test`

### 1.2 Run Projects
* **CLI App**: `dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- [command] [options]`
* **MCP Server**: `dotnet run --project src/InkTag.Mcp/InkTag.Mcp.csproj`
* **GUI App**: `dotnet run --project src/InkTag.Gui/InkTag.Gui.csproj`

### 1.3 Git Branching Strategy
* **Default to Feature/Fix Branches**: NEVER write code or make modifications directly on `main`. Always check out or create a dedicated feature or bugfix branch (e.g., `feat/feature-name` or `fix/bug-description`) before modifying files, unless the request is explicitly part of an active work session on the current branch.

### 1.4 Task & Issue Tracking Rules
GitHub Issues serve as the single source of truth and central backlog for all pending, deferred, and ongoing work across the repository.

1. **Mandatory Issue Capture for Deferred Work**:
   * Never leave deferred work, discovered bugs, architectural flaws, technical debt, or follow-up ideas in ephemeral chat messages or hidden in-code `TODO`s.
   * Whenever an issue, bug, refactoring need, or feature cannot or should not be addressed immediately within the current task scope, proactively create a GitHub Issue:
     ```bash
     gh issue create --title "<type>(<scope>): <clear title>" \
       --label "<labels>" \
       --body "<structured description>"
     ```
   * Title Prefixes: `fix(...)`, `feat(...)`, `perf(...)`, `refactor(...)`, `tech-debt(...)`, `docs(...)`.

2. **Standard Issue Structure**:
   * **Problem / Context**: Current behavior, deficiency, or missed requirement.
   * **Proposed Solution / Direction**: Suggested implementation path, architectural guidance, or alternatives.
   * **Affected Files / Modules**: Specific file paths, classes, or CLI/MCP/GUI components.
   * **Acceptance Criteria**: Concrete checklist of what done looks like.
   * **Origin / References**: Reference related issues, commits, review findings, or conversation context.

3. **Triage & Labeling**:
   * Apply appropriate labels: `bug`, `enhancement`, `deferred`, `tech-debt`, `documentation`.

4. **Issue-Driven Development Workflow**:
   * **Backlog First**: Before planning or starting significant work, check active issues (`gh issue list`) to avoid duplicate work and leverage existing context.
   * **Active Issue Context**: When starting work on an issue:
     1. Inspect the issue: `gh issue view <issue-number>`
     2. Create a dedicated branch linked to the issue (e.g., `fix/issue-20-backup-manifest`).
   * **Resolution**:
     * Reference the issue in commit messages: `Fixes #<issue-number>: <description>` or `Closes #<issue-number>: <description>`.
     * Note the issue number in `CHANGELOG.md` entries where applicable.
     * Ensure the issue is closed upon merging or resolving.

---

## 2. Coding & Design Standards

### 2.1 General C# Style Guidelines
* **Language Level**: C# 10.0 / .NET 10.0 (Nullable references enabled).
* **Namespaces**: Use file-scoped namespaces (e.g. `namespace InkTag.Core;`, `namespace InkTag.Cli;`, `namespace InkTag.Mcp;`, `namespace InkTag.Gui;`).
* **Naming Conventions**:
  * Classes, Interfaces, Methods, Properties: `PascalCase` (e.g., `ComicScannerService`, `ReadMetadata`).
  * Parameters, Local Variables: `camelCase` (e.g., `directoryPath`, `cancellationToken`).
  * Private Fields: Prefixed with underscore `_camelCase` (e.g., `_coverCache`).
* **Asynchronous Programming**:
  * Use `async`/`await` extensively for file/network I/O.
  * Always propagate `CancellationToken` to asynchronous tasks.
  * Execute background work on the ThreadPool (e.g., `Task.Run`) to prevent blocking UI main threads.

### 2.2 MVVM Conventions (CommunityToolkit.Mvvm)
* **ViewModels**: 
  * Must inherit from `ObservableObject` or `ObservableValidator` (for field validation).
  * Use C# Source Generators: annotate fields with `[ObservableProperty]` to generate properties, and methods with `[RelayCommand]` to generate commands.
  * Class names must end with `ViewModel` (e.g., `ComicItemViewModel`).
* **Views**:
  * Keep code-behind views clean. Views should only handle UI lifecycle, file/folder pickers, and raw windows events.
  * UI Logic must reside in the ViewModels.

---

## 3. Wiki Maintenance Rules (Layer 2 Schema)

As an AI Agent, you are responsible for maintaining the project's **Layer 2 Wiki** (located under [docs/wiki/](file:///home/aurora7795/AntiGravProjects/InkTag/docs/wiki/)). You must adhere to the following rules:

1. **Auto-Update**: Whenever you modify code (e.g., adding a new metadata field, changing validation rules, introducing a new service), you must read and update the corresponding wiki file in `docs/wiki/` to reflect these changes.
2. **Concept Cross-Linking**: Always cross-reference wiki pages using relative markdown paths (e.g., `[Metadata Editor](core_editor_api.md)`).
3. **Index Registry**: If you create a new wiki page, you MUST register it inside [docs/wiki/index.md](file:///home/aurora7795/AntiGravProjects/InkTag/docs/wiki/index.md).
4. **No Placeholders**: Never write placeholders, dummy comments, or TODOs in the wiki. Maintain complete, actual API surfaces, architectures, and guidelines.

---

## 4. Changelog Maintenance Rules

Maintaining [CHANGELOG.md](CHANGELOG.md) is a **MANDATORY requirement** for this repository:

1. **Keep Up to Date**: Every feature, bugfix, architectural improvement, or release MUST update `CHANGELOG.md` under the `[Unreleased]` section (or the new version header when preparing a release).
2. **Standard Format**: Follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) categories:
   - `### Added` for new features.
   - `### Changed` for changes in existing functionality.
   - `### Deprecated` for soon-to-be removed features.
   - `### Removed` for now removed features.
   - `### Fixed` for any bug fixes.
   - `### Security` in case of vulnerabilities.
3. **Release Protocol**: When bumping versions, convert the `[Unreleased]` section into the new release header (e.g. `## [0.12.0] - YYYY-MM-DD`) and ensure an empty `## [Unreleased]` section remains at the top.
