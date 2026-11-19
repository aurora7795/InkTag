# Workflow: /startcycle

## Step 1: Planning Phase
- Trigger the `Technical Project Planner` skill.
- Review existing GitHub issues (`gh issue list`) to incorporate pending backlog work and avoid duplication.
- File GitHub issues (`gh issue create --label "deferred"`) for any out-of-scope ideas or requirements identified for future iterations.
- Wait for `Technical_Specification.md` to be fully generated and verified as an Artifact.

## Step 2: Generation Phase
- Invoke the core Antigravity developer agent to read the spec and scaffold out the modular files on a dedicated branch.

## Step 3: Audit Phase
- Trigger the `Quality Assurance Auditor` skill to validate the output against the plan.
- For any edge cases, non-blocking defects, or technical debt identified during audit that are not fixed immediately, file them as GitHub issues (`gh issue create`) before completing the cycle.
