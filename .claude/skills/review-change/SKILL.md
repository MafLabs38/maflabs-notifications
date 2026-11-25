---
name: review-change
description: Run the four review agents (tests, quality, OWASP security, architecture) in parallel on the current branch before opening or updating a PR, then triage and apply their findings.
---

# Review a change before its PR

1. Base: `develop` (or the given base). Make sure the branch is committed and the test suite is green.
2. Launch **in parallel**, each with the base and the branch name, asking for its Markdown report:
   - `test-reviewer`: relevance, redundancy, missing cases, speed;
   - `quality-reviewer`: spec compliance, design, smells, conventions;
   - `security-reviewer`: OWASP review;
   - `architecture-reviewer`: philosophy, patterns, layers, hard-coded vs configurable, evolvability.
   If the agents are not registered in the session, spawn general-purpose agents with the content of `.claude/agents/<name>.md` as instructions.
3. **Triage** the reports (and the CodeQL and SonarQube findings) with the `review-triage` protocol (`templates/quality/review-decisions/`): fix what is right; decline medium and below with the reason in one line; a blocker, critical, high or major finding that you would not fix is the maintainer's to accept, so tell them and wait for their comment. Do not accept a finding without checking it in the code.
4. Re-run the tests, note the durations before and after.
5. Add a **Review** section to the PR description (and the entries of `docs/review-decisions/<pr>.md`): one table, per finding the agent, the normalised severity, the answer and the reason; test count and duration before/after.

The same agents run in CI (`.github/workflows/ai-review.yml`) as advisory PR comments; this skill is the local, triaged version.
