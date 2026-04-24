# Changelog

## [0.1.4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/compare/v0.1.3...v0.1.4) (2026-04-24)


### Features

* **scheduling:** use ConcurrentHeapSpanDictionary for InMemoryJobStore ([#21](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/21)) ([d1ed6f1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/d1ed6f1bbf8d7383727aad36f289c1203dc9a2d6))
* telemetry spans, typed JobId, FSM, Outbox bridge + NuGet isolation ([6a2feca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/6a2feca3166d6e2eab8ad1dbd49b5e1aa6c5b9f1))
* telemetry, typed JobId, JobStatusFsm, Outbox bridge + NuGet isolation ([5c49338](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/5c493380ffb1c3a20925f4fdc7b9a12c675b1ca0))


### Bug Fixes

* update dashboard and benchmarks to typed JobId ([ccd436c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/ccd436c97c1bdd4edb6f6a27eb6c2c8466d6b7bb))
* update ZeroAlloc.Outbox to 1.2.0 (contains OutboxMessageId) ([72e9129](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/72e91290069ae70e6b7264dd71e321d37c283b22))
* update ZeroAlloc.Outbox to 1.2.1 (OutboxMessageId in public API) ([016ad53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/016ad531aafa8a8266f0e83b1c4e3ddfe87c78f3))
* use JobId.New() in AotSmoke sample (JobContext.JobId is typed JobId) ([eac90df](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/eac90df16583c437751c19b60d952e4d9d4c6d75))

## [0.1.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/compare/v0.1.2...v0.1.3) (2026-04-24)


### Bug Fixes

* **dashboard:** also fix desktop Delete clipping and 4+1 card reflow ([#13](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/13)) ([9a41aab](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/9a41aabb3c6b1fa730b2452973ef116a8c3d594c))
* **dashboard:** fit jobs table at tablet viewports; hide ID column &lt; 900px ([#11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/11)) ([126b274](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/126b2744a5045cf14b2fe72f3a2c1011b7015c45))

## [0.1.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/compare/v0.1.1...v0.1.2) (2026-04-23)


### Performance

* add BenchmarkDotNet project measuring IJob.ExecuteAsync overhead ([#8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/8)) ([366409d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/366409dd0444735b1321f57a64b017f6f9766a4c))


### Documentation

* reference new JobExecute benchmark project ([#9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/9)) ([07ff167](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/07ff167ae7f88ad8665d979535fb93642523d6f3))


### CI

* add AOT publish smoke test (item 1 of [#5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/5)) ([#6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/6)) ([de3c5f0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/de3c5f04fd0d3021a8a71d909746343123d085c9))

## [0.1.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/compare/v0.1.0...v0.1.1) (2026-04-22)


### Features

* **blazor:** add JobsDashboard Blazor component and JobsDashboardClient ([b1ccae3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/b1ccae35691d86153a7bbd8c486a3f9a525df4b5))
* **core:** add core contracts — IJob, IJobStore, IScheduler, JobEntry, JobAttribute ([fad239e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/fad239e30951ec67269b5ab37901b872c6566f4f))
* **core:** add DefaultJobSerializer using System.Text.Json ([12b009a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/12b009aa9abd8c17378965a709f2435317b81e21))
* **core:** add DefaultScheduler, SchedulingWorkerService, and DI extensions ([a32a41a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/a32a41a44bbbc814f45c058441ae1461fb3bcecc))
* **dashboard:** add Minimal API, embedded HTML/JS UI, IJobDashboardStore ([bdbc88a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/bdbc88aa7ed8df080d651287fc681a7a84344466))
* **efcore,redis:** implement IJobDashboardStore on EfCoreJobStore and RedisJobStore ([71e08e9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/71e08e989e7dcf54ab9cfe89dae1bf881a5f5bf6))
* **efcore:** add EfCoreJobStore and SchedulingDbContext ([1552969](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/1552969555a6756ccc98dd91da80a0e8bf6c4831))
* **generator:** auto-detect IRequest&lt;Unit&gt; and emit MediatorJobTypeExecutor registration ([3e439a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/3e439a291fb512ecd7e11f4d0855eb3009ee7716))
* **generator:** emit IJobTypeExecutor, DI extension, recurring startup hosted service ([b62e94b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/b62e94b0acffeacd6459dea52d5fffb75dd1a51e))
* **inmemory:** add InMemoryJobStore ([f868224](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/f868224ae9405fa1652245acb957251b9b946f5f))
* **mediator:** add MediatorJobTypeExecutor and bridge extension ([d2cdc1b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/d2cdc1bd2c83bd04f373921abbc6e8da2f59ec50))
* **redis:** add RedisJobStore ([4c38eaa](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/4c38eaa8af11de71c8656ddefe98b3f5565b01ac))


### Bug Fixes

* **blazor:** fix BaseAddress, HTTP error handling, and code quality issues ([9edde82](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/9edde82f18036a3dc4455221d9b486a47598ef18))
* **ci:** add --skip-duplicate and dotnet tool restore to release.yml ([1bf4695](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/1bf46956914baf95b18df029f6e94a512f785ef1))
* **core:** eliminate IServiceScope leak in SchedulingWorkerService ([945ac24](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/945ac244d01bae457bdc1d72c6eca9c45c11c689))
* **core:** IOptionsMonitor hot-reload, per-job MaxAttempts, executor cache, UpsertRecurring consistency ([7561c2d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/7561c2d764aa49399b1c91ae1ba133e3ae26b511))
* **dashboard:** UI polish — Dead Letter label, empty state, mobile responsive, max-width container ([a34edfc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/a34edfc305c81d3f9e8f3a3d06f5d88742affbe9))
* **efcore:** atomic FetchPendingAsync via ExecuteUpdateAsync conditional claim ([105e877](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/105e87746ba27021ddf2233e7b3f20757f90a215))
* **efcore:** UpsertRecurring guards Running status; FetchPending uses ToJobEntry ([7bd0fcc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/7bd0fcc95bad2f9f53a2e743565a7cf4e682c208))
* **mediator:** remove incorrect AOT annotations from MediatorJobTypeExecutor ([36924b6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/36924b613808964b14dbe6f2dad563fbcf77a8dc))
* **packaging:** publish Z logo icon to NuGet ([#2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/2)) ([24e2d8c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/24e2d8c27a69bf8e4ab169cd5a87e48fbc9e8c04))


### Documentation

* add dashboard.md ([0138840](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/0138840be244aa5278c342b841259a65d7f48187))
* add diagnostics.md (ZASCH001 reference) ([232ccc4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/232ccc44400ac775f619027de9b85779838232e5))
* add docs/CI parity implementation plan ([366927b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/366927b9b7b09a5e6fd7f242851c15e15a472bec))
* add docs/index.md ([b4e7e77](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/b4e7e77ca1630c663246965ba9f746c927c02e49))
* add getting-started.md ([0724195](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/07241950dd843f01bde05ab05103364113cf3077))
* add mediator-bridge.md ([4be2739](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/4be27397165c947b6d94f62ca3e0b0de9d23017b))
* add performance.md ([56908e1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/56908e1f5b54ffd97d7384c5e8250aacefc6798b))
* add README.md ([8908e8a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/8908e8a75eff0af7e7e612b29823570e7172f09b))
* add source-generator.md ([816d401](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/816d4019fe99595a5e17fc11dfa9a0f678d22064))
* add stores.md (InMemory, EF Core, Redis backends) ([c472950](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/c4729506dae1cce4750321cc5414087897e612aa))


### Tests

* add EveryToCron, DefaultScheduler, InMemory dashboard, and Dashboard API endpoint tests ([cc8062a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/cc8062ab8f6d5f87f525cdd5cae9632445c0c910))
* **generator:** add negative assertion — fire-and-forget must not emit IHostedService ([9f1a47b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/9f1a47b8ae82240d30d9d1aee7f8738c16791f1d))


### CI

* add GitHub Actions workflows and release infrastructure ([0be6de4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/0be6de49e57723319343565eaf5be829e27d4973))
* align renovate config with ecosystem standard, add PR template ([afaf676](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/afaf676438fe25c0b014612b490a3342974c07ca))
* fix release-please config structure, standardise website trigger event-type ([bd766f8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/bd766f8576b479d7db57a81a72bb6b6c24be9d6f))
* **release-please:** add permissions block so token can create PRs ([#3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/3)) ([bc9e3a1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/bc9e3a11930c71172fd56b009f85c7eeb09a315c))
* upgrade release workflow, add GitHub Release artifact upload ([51b1ed4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/51b1ed4d1228c7dd179af60e8b33ce9f03b04cad))
* upgrade release-please.yml to checkout@v6, setup-dotnet@v5, fix PackageVersion ([6b4ef4a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/6b4ef4a943d6094275d4efaf310d7179d76bc1d3))
* upgrade to actions/checkout@v6, setup-dotnet@v5, add workflow_dispatch ([d93305f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/d93305fb6364ab155e45df2d9d91ba09a1179b14))


### Chores

* add CHANGELOG.md placeholder for release-please ([501f88c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/501f88cc211867b8160d1bfd30904ac7f770b1fd))
* add MIT License ([b446fe7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/b446fe774f5a2c9c3b16f32e1784151e1a4ae909))
* add NuGet package icon ([f664616](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/f664616471b11267cc8ae4e79a1b557304a0729c))
* fix code review issues — pin package versions, remove duplicate properties ([a76ac11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/a76ac11af957a248adfd7f0cbfe8a982ef94396a))
* initial project scaffold ([c107b32](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/commit/c107b328264934922003a2b460d24385d422a93e))

## Changelog
