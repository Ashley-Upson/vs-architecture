# Architecture V6 Work

Before changing Architecture planner, routing, sizing or Draw.io renderer code,
read `docs/architecture-v6-contract.md`. Treat it as the normative authority
for stage ownership and product rules.

Every implementation tranche MUST end with a `V6 Contract Compliance` report
that lists affected sections, preserved rules, intentional rule changes and
known remaining violations. If behaviour intentionally changes, update the
contract in the same tranche.

Integration runs SHOULD use the preserved user configuration in
`C:\Users\Ash\Documents\codex-artifacts\architecture-user-settings.json`
unless an explicit settings path is supplied.
