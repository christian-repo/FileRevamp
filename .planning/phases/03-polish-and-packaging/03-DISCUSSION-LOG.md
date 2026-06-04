# Phase 3: Polish and Packaging - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-03
**Phase:** 3-polish-and-packaging
**Areas discussed:** Edge-case test scope

---

## Edge-Case Test Scope

**Question 1: What coverage does Phase 3 add for the 3 ROADMAP edge cases?**

| Option | Description | Selected |
|--------|-------------|----------|
| Integration tests only | CommandAppTester through full CLI — catches wiring gaps | ✓ |
| Unit + integration | Extend unit tests AND add CLI-level tests | |
| Unit tests only | Extend unit tests in CollisionResolver, WildcardCompiler, RenameOrchestrator | |

**User's choice:** Integration tests only
**Notes:** Phase 2's code review found that FailureLogger was built but not wired — full CLI-level testing catches this class of issue.

---

**Question 2: Beyond the 3 ROADMAP cases, any additional scenarios?**

| Option | Description | Selected |
|--------|-------------|----------|
| The 3 ROADMAP cases only | Dots/parens in patterns, batch collisions, log-file exclusion | |
| Also: empty directory | Running against empty dir exits 0 with zero results | |
| Also: unicode + long filenames | Accented chars, near-MAX_PATH | |
| All of them | All scenarios above | ✓ |

**User's choice:** All of them
**Notes:** User wants comprehensive coverage including unicode and long filenames.

---

**Question 3: For unicode and long filenames — what's the test expectation?**

| Option | Description | Selected |
|--------|-------------|----------|
| Should work, test for success | Unicode/long paths rename correctly; no error on near-MAX_PATH | ✓ |
| Should fail gracefully | Log the failure, continue rather than crash | |
| You decide | Researcher and planner determine based on .NET 9 behavior | |

**User's choice:** Should work, test for success
**Notes:** These are valid real-world inputs on Windows; .NET 9 handles unicode paths natively.

---

## Areas NOT Discussed (user's choice)

- **Integration test architecture** — Not selected for discussion; left to researcher/planner
- **Package metadata & publish target** — Not selected for discussion; left to researcher/planner

## Claude's Discretion

- Specific test method names and assertion wording
- Whether to add a `Makefile`/`just` recipe for pack/install convenience
- Exact approach for MAX_PATH testing (real 255-char names vs simulated)
- Integration test architecture (IFileSystem injection vs temp dirs vs orchestrator level)
- NuGet package metadata (PackageId, Authors, Description, license, RepositoryUrl)

## Deferred Ideas

None — discussion stayed within phase scope.
