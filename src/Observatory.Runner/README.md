# Observatory Runner

.NET CLI HTTP client for one API demo resource. It never calls an inference SDK.
Every command requires an explicit API `--base-url` and local `--artifacts` directory.
No default output directory, credentials, or secret exports.

```powershell
dotnet run --project src\Observatory.Runner -- smoke --base-url http://localhost:5101 --artifacts artifacts\inline
dotnet run --project src\Observatory.Runner -- dryrun --base-url http://localhost:5101 --artifacts artifacts\inline --models gpt5,gpt6-astra,gpt6-sol,gpt6-luna
dotnet run --project src\Observatory.Runner -- benchmark --base-url http://localhost:5101 --artifacts artifacts\inline
dotnet run --project src\Observatory.Runner -- export --run-id <32-hex-id> --base-url http://localhost:5101 --artifacts artifacts\inline
```

`benchmark` and `dryrun` only calculate plans; they never execute inference.
`smoke` verifies configuration and demo data without submitting a chat turn.
Additional options: `--scenarios main-six-turns`, `--repetitions 1`, `--prompt good`,
`--history full`, `--transport direct`, `--max-calls 24`, `--max-output 1500`,
`--timeout-seconds 180`. Model profiles match the frozen Core catalog.
For heterogeneous matrices use `--configurations '[{"mode":"live","promptProfile":"bad"},{"mode":"live","promptProfile":"good"}]'`
instead of individual configuration flags. Smoke accepts exactly one configuration.
Batch execution is blocked because
the frozen experiment contract does not authorize an aggregate batch budget.
No retry can accidentally submit a paid request.

Smoke checks health, live-only configuration and demo data without inference.
Benchmark dry-run artifacts include every planned case and run's sanitized evidence bundle.
Exit codes: `0` success, `1` invalid options/transport/check failure, `2` executed case failures.
