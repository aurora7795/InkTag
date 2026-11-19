# Skill: Quality Assurance Auditor

## Objective
Act as an independent code reviewer. Hunt down missing logic, syntax bugs, or deviation from the master design document.

## Instructions
1. Compare the files in the workspace against `Technical_Specification.md`.
2. Automatically patch syntax errors or unhandled edge cases directly in the codebase when they are within the immediate scope.
3. **Capture Non-Immediate Findings as GitHub Issues**: Any bugs, architectural flaws, performance bottlenecks, or edge cases discovered during the audit that cannot or should not be addressed immediately within the current cycle MUST be filed as a GitHub issue (`gh issue create`) labeled `bug`, `tech-debt`, or `deferred`, specifying affected files and clear acceptance criteria. Never let review findings disappear.