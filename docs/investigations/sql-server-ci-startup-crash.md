# SQL Server containers crash at startup in CI

Finding only. The crashes are an engine bug in the pinned SQL Server 2025 image.
Microsoft's fix is SQL Server 2025 CU12, and that image is not on MCR yet, so this
change does not move the pin, share a container, or retry a start. ADR-0035 forbids
a retry and a longer timeout.

## Cause

The pinned image is SQL Server **17.0.4025.3**, tag `2025-CU3-ubuntu-24.04`, built
2026-02-26. Every `MsSqlBuilder` uses this digest:

`mcr.microsoft.com/mssql/server:2025-latest@sha256:ca62ad2d75975074cbc9ac39a1329cd88af9cd3ea2ffb59d6302579a97cf32f4`

On some starts the `sqlservr` process dies about two to three seconds after the line
`SQL Server 2025 will run as non-root by default`, runs `/opt/mssql/bin/handle-crash.sh`,
and the container exits 1. Testcontainers then throws `ContainerNotRunningException`.
xUnit reports every test on that fixture as failed in 1 ms, because the class fixture
is already dead. The container's own report says `Distribution: Ubuntu 24.04.4 LTS`
and build stamp `869cbfe2a28bbc5800059f48feda967144643fd64d79a439b5c6333420172b72`.

The three crashes in the survey are three signatures of that startup failure:

1. Run [43396128951](https://github.com/sabysabashvily/EclipsVault/actions/runs/43396128951) attempt 1, commit `78fafb58`, class `SqlServerShareIssuerBackfillTests`, all five tests. The container log at 12:51 Asia/Tbilisi on 13 Nov 2026:

```text
** ERROR: [AppLoader] Failed to load LSA: 0xc0000043
AppLoader: Exiting with status=0xc0000043
Reason: 0x00000006
Message: Termination of \SystemRoot\system32\AppLoader.exe was due to fatal error 0xC0000001
Last errno: 2
Last errno text: No such file or directory
Processors: 4
Total Memory: 16759443456 bytes
```

The non-root banner is `2026-11-13T08:51:49.583Z` and the LSA line is `2026-11-13T08:51:52.052Z`.

2. The same run, attempt 2, classes `SqlServerEmailIdentityPersistenceTests` and `SqlServerEmailIdentityUpgradeTests`, at 15:21 Asia/Tbilisi. The banner is `11:21:39.524Z` and the fatal line is `11:21:41.632Z`:

```text
Reason: 0x00000004
Message: ASSERT: Expression=(!currentWorker->IsPreemptiveOnEntry()) File=LibOS\Windows\Kernel\NtUm\NtumWaiter.cpp Line=444
Last errno: 2
Last errno text: No such file or directory
Total Memory: 16759435264 bytes
```

3. Run [42638692366](https://github.com/sabysabashvily/EclipsVault/actions/runs/42638692366) attempt 1, commit `dbe59d291d`, the same two email classes, at 18:53 Asia/Tbilisi on 9 Nov 2026. This is signature B from [microsoft/mssql-docker#974](https://github.com/microsoft/mssql-docker/issues/974): `Reason: 0x00000002`, parameter `[0] 0x10010`, stack through `lsasrv.dll` into `lsass.exe`, same build stamp. Attempt 2 of that run passed.

[microsoft/mssql-docker#974](https://github.com/microsoft/mssql-docker/issues/974) reports the same startup death on `2025-CU9-ubuntu-24.04` and on `2025-latest` while that tag was CU9. Signature A there is `Reason: 0x00000006`, fatal error `0xC0000001`, `Last errno: 2`, and `Failed to load LSA` (their build prints `0xc0000001`; CU3 prints `0xc0000043`). Signature B is the `0x00000002` / `0x10010` / `lsass.exe` crash, which our 9 Nov log matches frame for frame, shifted by the older `sqlpal.dll`. Microsoft closed that issue on 19 Oct 2026 with: the fix ships in SQL Server 2022 CU29 and SQL Server 2025 CU12. Their measurement, on a runner with several GB free, was about 1.7% of sequential starts and about 8.3% when three containers started together. A single container still crashes. They ruled memory out.

MCR on 13 Nov 2026 has no `2025-CU12` tag. `2025-latest` is CU11, version **17.0.5065.9**, created 2026-11-05, digest `sha256:a554b8cf6ab60b92838737f03d0422c4babcd491d09cba3bec0e5e41e292b921`. CU11 is the newest published update, and it is the update Microsoft had not yet called the fix.

## Which fixtures start SQL Server

`VaultFixture` starts one SQL Server through `SqlServerVaultDatabase`. These fixtures each construct another `VaultFixture`, so each starts a server of its own. Their collections set `DisableParallelization = true`, so each runs while no other collection runs:

- `AuditFailureFixture`
- `ClosedProductionWindowFixture`
- `SingleAdministratorFixture`
- `KekRotationFixture`
- `MailboxFixture`
- `RedisVaultFixture`

Six more fixtures construct `MsSqlContainer` directly. None of them sets `DisableParallelization`. xUnit 2.9.2 has no `xunit.runner.json` here, so collections run in parallel up to `Environment.ProcessorCount`. The runner reports 4 processors, and the crash dump reports `Processors: 4`. At most four of these servers are up at once:

| Fixture | Test classes on that one server |
|---|---|
| `VaultFixture` (`VaultCollection`) | the shared HTTP suite |
| `SqlServerShareDatabases` | `SqlServerShareSaveFailureTests`, `SqlServerConcurrentAccessRequestTests`, `SqlServerConcurrentShareTests`, `SqlServerAccessRequestSaveFailureTests` |
| `SqlServerEmailIdentityDatabases` | `SqlServerEmailIdentityPersistenceTests`, `SqlServerEmailIdentityUpgradeTests` |
| `SqlServerAccountChangeDatabases` | `SqlServerAccountTransactionFailureTests`, `SqlServerAdministratorRaceTests`, `SqlServerApiKeyUseTests`, `AuthenticationSharingTests`, `SqlServerCredentialGenerationPersistenceTests`, `SqlServerDelayedProfileCommitTests`, `SqlServerMailboxPersistenceTests`, `SqlServerMailboxSendLimitTests`, `SqlServerProfileOperationPersistenceTests`, `SqlServerAccountChangeRaceTests`, `SqlServerAccountCreationRaceTests` |
| `SqlServerShareIssuerBackfill` | `SqlServerShareIssuerBackfillTests` (class fixture, its own collection) |
| `SqlServerPendingDenialReasonsCleanup` | `SqlServerPendingDenialReasonsCleanupTests` (class fixture, its own collection) |

A SqlServer job therefore starts 12 SQL Server containers over the run: the six exclusive hosts, one after another, and the six parallel fixtures, up to four at a time. The green run of `0a44040` (run 43415890636) started 12 and peaked at 4, same as the crashing attempt. Attempt 2 of `dbe59d291d` also peaked at 4 and passed. Four at once is the normal schedule, and it usually survives.

No builder calls `WithMemory` or sets `MSSQL_MEMORY_LIMIT_MB`. With no cgroup limit, SQL Server's documented default target is 80% of the memory it detects. The crash dump's `Total Memory` is 16759443456 bytes, 15.61 GB, which is the runner, so the target is about 12.5 GB per process. Microsoft's minimum to start the engine is 2 GB. The process dies before it has taken that target: `Last errno` is 2 (`ENOENT`), the dump still shows the full 15.61 GB, and in the same second other SQL containers answer `sqlcmd` and the vault answers HTTP. The PostgreSQL job of run 43396128951 passed on both attempts.

## Last 50 CI runs

`gh run list --workflow ci.yml --limit 50`, surveyed 13 Nov 2026. One run, 43456263613, was still in progress and has no SqlServer log. The other 49 runs produced 53 completed `integration (SqlServer)` jobs (four runs have a second attempt). Every job that printed a runner image printed `ubuntu-26.04`. Forty-eight that reached Docker printed kernel `7.0.0-1019-azure`. Two printed `7.0.0-1016-aws`, and both passed. Docker on the crashing jobs was 29.4.2.

Jobs whose log contains `Failed to load LSA`, `handle-crash.sh`, or `ContainerNotRunningException`:

| Run | Attempt | Commit | Classes whose tests all failed | Signature |
|---|---|---|---|---|
| 43396128951 | 1 | `78fafb58cd525682ce1bd202a45fd7c26f7bd7a3` | `SqlServerShareIssuerBackfillTests` (5 tests, each `[1 ms]`) | LSA `0xc0000043`, Reason `0x00000006` |
| 43396128951 | 2 | `78fafb58cd525682ce1bd202a45fd7c26f7bd7a3` | `SqlServerEmailIdentityPersistenceTests`, `SqlServerEmailIdentityUpgradeTests` (26 tests) | Reason `0x00000004`, `NtumWaiter.cpp:444` |
| 42638692366 | 1 | `dbe59d291d3ea98f4a2bfc85abfbfc041c25665e` | `SqlServerEmailIdentityPersistenceTests`, `SqlServerEmailIdentityUpgradeTests` (26 tests) | Reason `0x00000002`, parameter `0x10010` |

Counts of crashing jobs per class: `SqlServerEmailIdentityPersistenceTests` 2, `SqlServerEmailIdentityUpgradeTests` 2, `SqlServerShareIssuerBackfillTests` 1. No other class failed this way. Per commit: `78fafb58` crashed in 2 of 2 SqlServer jobs; `dbe59d291d` crashed in 1 of 2; every other commit in the 53 jobs crashed in 0.

Three other SqlServer jobs failed before any container started, on `NU1902` for `System.Formats.Cbor`: runs 43109226998, 43115812905, and 43302359759.

The runner image does not pick the victims. The same `ubuntu-26.04` image and the same kernel ran the 50 jobs that did not crash, including the next commit on `main`. The digest does: every crash prints the CU3 build stamp, and the published defect is that image's SQLPAL startup race, which concurrent starts make more likely. Putting the six parallel fixtures on one server would drop the peak from four to two and leave the race in place. CU12 is the change that removes it, and it is not published.
