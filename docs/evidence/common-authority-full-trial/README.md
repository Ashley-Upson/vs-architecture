# Consolidated development-authority trial

The controlled input is the deduplicated cCoder project with
`artifacts/node-duplication/real-project-deduplicated-settings.json`. The CLI was rebuilt before the trial.

```powershell
dotnet run --project src\StandardIo.ArchitectureDiagram.Cli\StandardIo.ArchitectureDiagram.Cli.csproj -c Release --no-build -- "C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement\cCoder.ContentManagement.csproj" --settings "C:\Users\Ash\Documents\standard-io\artifacts\node-duplication\real-project-deduplicated-settings.json" --renderer drawio --development-common-authority-trial "C:\Users\Ash\Documents\standard-io\artifacts\consolidated-authority-trial"
```

`before.drawio` retains the normal production SHA-256
`08D70BBA59130F8D56EC4F411D3A5BB360B6FB1BBA800D5C43FE1A6386DAB7F6`.
`after.drawio` has SHA-256 `DDEBECAFA239CD99CBD26F9D78242F8C9BFABE084F5CF5F641B5D741428B2458`.

## Result

- 250 links were locally eligible across downward, same-layer, and upward topology production.
- 28 interaction-closed components were eligible; 26 components containing 27 links passed mixed validation.
- One component was rolled back after node-collision, shared-segment, crossing, and spacing findings.
- One mixed interaction boundary and the only proposed depth-2 movement were rejected before execution.
- Same-layer/upward observation found 32 same-layer and 42 upward links; 49 assignments were obstacle-safe.
- Strict traceability findings decreased from 1,658 to 1,657. Immediate reversals decreased from 34 to 33.
- No higher-priority strict finding count increased. Clean perpendicular crossovers increased by one.

## Movement closure

The depth-2 proposal requires a 60px lower-layer suffix movement, but it invalidates routes outside the supported
common-authority set. Its closure is therefore incomplete and persistent movement is forbidden. This is evidence
against applying that real proposal, rather than a reason to weaken the movement boundary. InterLayer 0->1 and
InterLayer 4->5 likewise remain mixed-family whole-band deficits; neither is an eligible common-only movement.

## Production isolation and performance

One warm-up and five fresh Release CLI processes all retained the exact production hash. The measured runs were
14,640-14,810ms with a 14,704ms median. Normal production authority and serialized output are unchanged.

No VSIX was packaged.
