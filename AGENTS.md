# AGENTS.md: ZeroAlloc.Scheduling

Source-generated background jobs for .NET: a `[Job]` class gets generated registration and executors, and a
worker runs one-off, delayed and cron jobs with retries. It ships as 11 NuGet packages: the core
`ZeroAlloc.Scheduling` (which bundles the generator), the standalone `ZeroAlloc.Scheduling.Generator`,
the stores `InMemory`, `EfCore`, `Orm` and `Redis`, the `Dashboard` and `Dashboard.Blazor`, and the
`Mediator`, `Resilience` and `Telemetry` bridges.

## Layout

- `src/ZeroAlloc.Scheduling.Generator` targets `netstandard2.0` and is the source generator. The core project
  references it as an analyzer and packs the DLL under `analyzers/dotnet/cs`. Tests reference it the same way.
- Libraries target `net10.0;net11.0`.
- Test projects target `net10.0;net11.0`, like the libraries. There is no Redis test project:
  Redis is covered by the AOT smoke only.
- `benchmarks/` targets `net10.0` only, not every library framework.
- `samples/ZeroAlloc.Scheduling.AotSmoke` is the NativeAOT publish check, not a demo. It targets `net11.0`
  and runs one scenario per package. The Redis scenario runs only when `ZA_SMOKE_REDIS` holds a connection
  string.
- Each package except `Dashboard.Blazor` has its own `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. The generator has
  `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` for its `ZASCH` diagnostics.
- `apicompat-suppressions.xml` lists the intended breaks of 3.0.0 against the 2.6.0 baseline: the dropped
  net8.0 and net9.0 builds. It is deleted once 3.0.0 is the published baseline.
- `docs/performance.md` and the README carry measured allocation figures.

## Build and test

Prerequisites: the SDK that `global.json` pins (11.0.100, roll forward to the latest minor), the .NET 10 and
.NET 11 runtimes for the tests, and for the AOT smoke `clang` and `zlib1g-dev`.

Job `build` in `.github/workflows/ci.yml`:

```bash
dotnet tool restore
VERSION=$(dotnet gitversion /showvariable SemVer)
dotnet restore
dotnet build --no-restore -c Release -p:Version=$VERSION
dotnet test --no-build -c Release --verbosity normal --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
```

The same job then runs the shared `coverage-gate` action over `TestResults/**/coverage.cobertura.xml`,
which fails below a line coverage floor. The commands above do not reproduce that floor. It also packs
every `src/*/*.csproj` without `<IsPackable>false</IsPackable>` with `dotnet pack --no-build -c Release
-p:Version="$VERSION" -o ./artifacts`.

Job `aot-smoke` in `ci.yml`, with a Redis 8 service on port 6379:

```bash
dotnet restore samples/ZeroAlloc.Scheduling.AotSmoke/ZeroAlloc.Scheduling.AotSmoke.csproj
dotnet publish samples/ZeroAlloc.Scheduling.AotSmoke/ZeroAlloc.Scheduling.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out
ZA_SMOKE_REDIS=localhost:6379 ./aot-out/ZeroAlloc.Scheduling.AotSmoke
```

Job `full` in `.github/workflows/benchmarks.yml` runs by hand only. The automatic `smoke` job there is the
shared `benchmark-smoke.yml` gate, which is not a plain command:

```bash
dotnet build benchmarks/ZeroAlloc.Scheduling.Benchmarks -c Release
dotnet run --project benchmarks/ZeroAlloc.Scheduling.Benchmarks -c Release --no-build -- --filter "${FILTER:-*}"
```

The `api-compat` and `release-tracking` jobs in `ci.yml` call shared workflows from `ZeroAlloc-Net/.github`.

Inner loop, each verified here. Pass a `-p:Version` value to every build:

```bash
dotnet build tests/ZeroAlloc.Scheduling.Tests -c Release -p:Version=9.9.9-local.abc1234
dotnet test tests/ZeroAlloc.Scheduling.Tests --no-build -c Release -f net10.0
dotnet test tests/ZeroAlloc.Scheduling.Tests --no-build -c Release -f net10.0 --filter "FullyQualifiedName~WorkerAllocationTests"
dotnet test tests/ZeroAlloc.Scheduling.Generator.Tests -c Release -f net11.0 -p:Version=9.9.9-local.abc1234
```

CI job `sdk10-consumer` needs a 10.0.1xx SDK; `tests/ZeroAlloc.Scheduling.Sdk10Smoke/global.json` pins
10.0.100 with `rollForward: latestPatch`, so a newer band cannot stand in for it. It builds and runs a
consumer of the packed packages with Roslyn 5.0:

```bash
dotnet pack src/ZeroAlloc.Scheduling -c Release -o artifacts -p:Version=9.9.9-local.sha$(git rev-parse --short HEAD)
dotnet pack src/ZeroAlloc.Scheduling.InMemory -c Release -o artifacts -p:Version=9.9.9-local.sha$(git rev-parse --short HEAD)
cd tests/ZeroAlloc.Scheduling.Sdk10Smoke/Consumer
dotnet run -c Release -p:ZeroAllocSchedulingVersion=9.9.9-local.sha$(git rev-parse --short HEAD)
```

CI job `roslyn-floor` calls the shared `roslyn-floor.yml` from ZeroAlloc-Net/.github: it packs the
solution and fails when a packed DLL references Roslyn above 5.0.0.

## Conventions and gotchas
- The generator references `Microsoft.CodeAnalysis.CSharp` 5.0.0, the compiler of SDK band 10.0.1xx, the
  oldest supported SDK. A compiler older than the Roslyn the DLL references skips the generator with
  `CS9057`, and nothing is generated. The shared Renovate preset holds it below 5.1, so the repo has no
  Roslyn rule of its own. `RoslynFloorTests` fails when the generator references anything newer than 5.0.
  If a newer Roslyn API is needed, change the code; raise the floor only together with the minimum SDK.
  The generator test project may use a newer Roslyn.

- **Versions.** The csproj files carry `<Version>0.0.0-local</Version>` as a placeholder. `dotnet pack` fails
  with `ZAVERSION001` or `ZAVERSION002` unless the release version reaches the build too. When you pack with
  `--no-build`, build with the same `-p:Version` first, as the `build` job does.
- **Public API is locked.** `RS0016` and `RS0017` are errors. A new public member goes into that package's
  `PublicAPI.Unshipped.txt`, a new generator diagnostic into `AnalyzerReleases.Unshipped.md`. Do not edit the
  `Shipped` files: the `ship-release-tracking` job moves entries on the release PR, and `release-tracking`
  fails the PR if it did not.
- **Analyzers.** `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on, with Meziantou, Roslynator and
  `ZeroAlloc.Analyzers` referenced from `Directory.Build.props`. `LangVersion` is `latest`.
- **Generator code.** It runs on `netstandard2.0` with `EnforceExtendedAnalyzerRules`, so no newer BCL API.
  Generator tests compile source with Roslyn and assert on diagnostics, hint names and incrementality, so a
  change to emitted code needs a test of the output, not only of the model.
- **Hot-path budget.** The worker allocates, per job beyond the store, exactly the `JobContext` handed to the
  job, for one-off and recurring jobs alike. `WorkerAllocationTests` asserts this and a recurring job must not
  re-parse its cron expression. Its `MeterListener` test sits in the same class on purpose, so it never runs
  alongside the allocation test: a listener enables the worker's instruments process-wide.
- **AOT smoke.** It runs with trim and AOT warnings as errors. A new package or store needs its own scenario
  in `samples/ZeroAlloc.Scheduling.AotSmoke`.
- **Packable set.** CI packs every `src/*/*.csproj` that does not set `IsPackable` to `false`, so a new
  project there ships. Add it to `ZeroAlloc.Scheduling.slnx` as well.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
