# Development-only full-diagram common-authority trial

The controlled input is the deduplicated cCoder project at source revision used by the existing routing audits, with `artifacts/node-duplication/real-project-deduplicated-settings.json`. The explicit CLI-only trial entry point produced:

- `before.drawio`: current production output, SHA-256 `08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`;
- `after.drawio`: accepted mixed-authority trial output, SHA-256 `88C539710C2387BF5ECD54C0DF6AEDC1712CA63AA9816356C27F06A7CC8E5DF1`;
- `trial-report.json`: route eligibility, component decisions, region extents, full validation, route quality, boundary interaction and timing evidence.

The command was:

```powershell
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- "C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj" --settings "artifacts\node-duplication\real-project-deduplicated-settings.json" --renderer drawio --development-common-authority-trial "docs\evidence\common-authority-full-trial"
```

## Result

There are 45 route-locally eligible routes, but the final-geometry closure produces only two fully eligible closed components. One one-route component is accepted. The second one-route component is rolled back because its common candidate introduces a shared segment with a legacy route. A large mixed component and 26 wholly unsupported components are rejected before execution. The trial retains 293 legacy routes and changes one route.

The accepted route changes from 516px/3 bends to 536px/2 bends. It is not flagged as unreasonable. The full hard-finding set changes only by removing one immediate reversal (34 to 33); all other requested categories are unchanged. No hard finding is introduced inside the accepted component or at its boundary.

## Deficient-band gate

The requested real deficient-band movement artefacts do not exist because the measured premise is false for the bounded route family. All seven common adjacent-downward allocation regions have zero missing extent. Available/required extents are:

| Lower depth | Available | Required | Missing | Demands |
|---:|---:|---:|---:|---:|
| 1 | 140 | 44 | 0 | 20 |
| 2 | 140 | 56 | 0 | 7 |
| 3 | 140 | 32 | 0 | 1 |
| 4 | 140 | 32 | 0 | 1 |
| 5 (ordinary) | 140 | 32 | 0 | 1 |
| 5 (lower band) | 140 | 32 | 0 | 13 |
| 6 | 118 | 32 | 0 | 2 |

The real whole-band deficits remain 132px at depth 0→1 and 168px at depth 4→5, but those demands include unsupported route families. Expanding either band as an “eligible common component” would partially rewrite a mixed interaction/movement boundary and violate the trial scope. The persistent `MinimumY` lower-suffix materializer is implemented and fixture-tested, but deliberately not invoked by this cCoder trial.

This is a blocker to production authority, not a reason to weaken closure or expand route-family support inside this tranche.

## Production isolation and performance

One excluded warm-up and five fresh Release CLI processes ran normal generation with diagnostics and telemetry disabled. Every output retained the accepted production hash. Measured times were 13,951–14,145ms with a 14,098ms median. Raw results are in `normal-generation-results.csv`. The preceding accepted median was 14,723ms, so dormant trial code caused no material normal-generation regression.
