# Contributing to Trailblazer

Thanks for helping improve Trailblazer. Focused bug fixes, tests, documentation,
benchmarks, and proposals that strengthen deterministic navigation are welcome.
Open an issue before a large or breaking change so its map, query,
serialization, and migration impact can be discussed first.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
See [Support](.github/SUPPORT.md) for help and [Security](SECURITY.md) for
private vulnerability reports.

## Development setup

Trailblazer uses the .NET 10 SDK for solution tooling and runs its test and
benchmark projects on .NET 10. From the repository root:

```bash
dotnet restore Trailblazer.slnx --property:Configuration=Release
dotnet build Trailblazer.slnx --configuration Release --no-restore
dotnet test Trailblazer.slnx --configuration Release --no-build
```

Repeat the same commands with `ReleaseLean` when a change touches public APIs,
serialization, dependencies, or packaging. Do not mix standard and Lean LSF
packages in one validation run.

To build the documentation after a Release build:

```bash
dotnet tool restore
dotnet tool run docfx docs/api/docfx.json --warningsAsErrors
```

Benchmark commands and evidence rules live in
[`tests/Trailblazer.Benchmarks/README.md`](tests/Trailblazer.Benchmarks/README.md).

### Coordinated source development and IDE restore

For coordinated unreleased LSF changes, use matching `Chronicler`,
`FixedMathSharp`, `SwiftCollections`, `GridForge`, and `Gravitas` sibling
checkouts. Published dependencies do not include those fixes. Command-line
invocations can pass `-p:UseLocalLsfStack=true` to restore/build/test.

This includes explicit source references at library, adapter, test, and
benchmark boundaries. `Directory.Build.props` owns the shared runtime version
selection and each library's reference identity. Trailblazer and its adapter use
`0.0.0-tb-local-lsf`; siblings use their corresponding evergreen labels. All
source assemblies use assembly/file version `0.0.0.0`, even when GitVersion
values are present. These identities do not change for releases.

Source-mode references to sibling projects disable inferred project references
so MSBuild uses the explicit dependency edges and their version metadata. Within
this solution, the core and adapter get their identity from the shared props
file, including when MSBuild reaches them through a project reference.

For VS Code design-time restore, fully exit **all VS Code windows** first, then
run the following from an external PowerShell terminal at the Trailblazer root:

```powershell
$env:UseLocalLsfStack = 'true'
dotnet restore Trailblazer.slnx -p:Configuration=Debug
dotnet build Trailblazer.slnx -c Debug --no-restore
code .
```

MSBuild reads this process environment property in every project, including
sibling repositories during NuGet restore. Newly launched VS Code services and
terminals inherit it. An existing VS Code process does not acquire a new
environment from `code .`, `--new-window`, or a terminal opened inside it; a
window reload alone is not sufficient. Repeat this launch when returning to
coordinated source work. This does not set a persistent user/machine variable or
change ordinary package-mode checkouts.

An `NU1102` for an unreleased Gravitas version means the IDE restore is still
selecting packages. A terminal-only `-p:UseLocalLsfStack=true` does not
configure its parent design-time service. Use the same mode for restore and
build. For package validation in a source-enabled shell, pass
`-p:UseLocalLsfStack=false` explicitly; for a package-mode IDE, exit it and
relaunch from a shell without the environment variable. Do not lower package
pins, hide unpublished fixtures in the global NuGet cache, or treat source-mode
packages as release artifacts.

Do not substitute a Trailblazer-only `Directory.Build.props` assignment for the
environment/global property. NuGet evaluates referenced sibling projects
separately: build-time reference metadata alone does not make their restore
graph select the same source stack.

## Pull request process

The [adapter README](src/Trailblazer.Gravitas/README.md) lists focused
test/coverage commands. Final validation against released dependencies remains a
separate gate.

Core and adapter coverage use separate runsettings. Never override the adapter
with the core-only include filter. CI requires both assemblies to be present and
fully covered. DocFX consumes both Release DLL/XML pairs.

1. Keep the change focused and preserve deterministic, engine-agnostic runtime
   behavior.
2. Add or update tests for meaningful behavior changes. Cover exact ordering,
   fixed-point cost, boundaries, failure status, and serialization where they
   are part of the contract.
3. Update public XML comments, the root README, the matching DocFX guide, and
   migration guidance when their contract changes.
4. Run focused checks first, then the full applicable `Release` and
   `ReleaseLean` matrix. Describe the exact results in the pull request.
5. Do not manually bump package versions. Release workflows derive versions
   through GitVersion.
6. Call out map publication, cache invalidation, action ownership, serialized
   schema, or package-family changes explicitly; these are high-risk boundaries.

For scheduled Navigator changes, verify moving-foot adoption and waiting motion,
not just ticket completion. A\* connector proofs belong to one lease and consume
the existing active-byte ceiling and fair scheduler validation turns. Include
shared-payload isolation, repeated host movement, mode changes, transactional
loads, and reset/disposal in lifecycle tests. See
[NavSteering](docs/api/guides/NavSteering.md#scheduled-acquisition-and-moving-origins).

For short, synchronous current-thread allocation assertions, use
[`AllocationTestUtility`](tests/Trailblazer.Tests/Support/AllocationTestUtility.cs).
The core test executable sets `ConcurrentGarbageCollection=false` so .NET
background-GC allocation-context accounting cannot inflate the byte counter.
Keep that test-host setting enabled. The non-inlined helper measures exactly one
invocation; it does not warm up, retry, force collection or reserve a no-GC
region. Callers own warmup and keep assertions outside the measured action. Real
allocations still count across blocking collections, and action exceptions
propagate unchanged. This setup does not change library/consumer or benchmark GC
settings and is not suitable for asynchronous/global allocation measurements.

## Documentation expectations

- Keep the root README concise and safe for both GitHub and NuGet rendering.
- Keep behavioral and integration guidance under `docs/api/guides`. DocFX
  publishes it beside the API reference after a successful `main` build.
- Keep DocFX configuration, landing content, namespace overrides, and theme
  files under `docs/api`. Never edit or commit generated `docs/api/obj` output.
- Use current public signatures in runnable examples. Label host placeholders,
  partial snippets, and engine-adapter pseudocode clearly.

## Cleanup and formatting

Restore the solution using the same dependency mode as the current build, then
run these checks from the repository root. Keep `UseLocalLsfStack=true` in the
shell environment when formatting a sibling-source build.

```bash
dotnet format Trailblazer.slnx --severity error --no-restore --include src tests
npx --yes prettier@3.9.5 --write .
```

Use `--verify-no-changes` with `dotnet format` and `--check` with Prettier for
read-only verification. Prettier excludes generated output, engineering plans,
and validation records.

Keep each namespace-level type or delegate in its own file. Put public and
internal nested types in companion files that preserve their declaring type.
Private nested helpers can stay with their owner. Summarize the primary type in
each file and use `<content>` to describe a secondary partial file. Use the
existing MIT header with the filename in authored `src` C# files. Test and
benchmark C# files omit the header.
