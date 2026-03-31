# Bootstrap

Use these helpers first when working in this repo:

- `tools/Build-RepoIndex.ps1`: generates a current file map for the repo.
- `tools/Build-NinjaTraderApiIndex.ps1`: snapshots the local NinjaTrader API surface relevant to this project.
- `tools/Build-DependencyMap.ps1`: generates the current project dependency map.
- `tools/Analyze-Gaps.ps1`: detects stale, missing, or generic repo-index coverage.
- `tools/Validate-Architecture.ps1`: enforces separation between engine, adapter, and rendering layers.
- `tools/Validate-Behavior.ps1`: runs scenario-based pure engine behavior tests.
- `tools/Check-GeneratedArtifacts.ps1`: verifies generated docs are current.
- `tools/Run-Verification.ps1`: runs the full maintenance and verification loop.

Recommended workflow:

1. Run `powershell -ExecutionPolicy Bypass -File .\tools\Build-RepoIndex.ps1`
2. Run `powershell -ExecutionPolicy Bypass -File .\tools\Build-NinjaTraderApiIndex.ps1`
3. Run `powershell -ExecutionPolicy Bypass -File .\tools\Build-DependencyMap.ps1`
4. Read `docs/REPO_INDEX.md`
5. Read `docs/NINJATRADER_API_INDEX.md`
6. Read `docs/DEPENDENCY_MAP.md`

Current intent:

- build and maintain `IntentLayerV01`
- use NinjaTrader Order Flow+ volumetric data where available
- keep the indicator modular: orchestrator, sampler/engine, models, renderer

Run `tools/Refresh-Indexes.ps1` after any structural change so generated docs replace stale repo metadata rather than drifting.
Run `tools/Validate-Behavior.ps1` when signal logic changes so the behavioral scenarios, multi-bar state transitions, and decision packets stay deterministic and explained.
Run `tools/Run-Verification.ps1` before major handoff points so gaps, drift, build failures, and architecture regressions are caught together.
