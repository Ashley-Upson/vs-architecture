# Architecture Duplication Contract

This document records the duplication behavior currently implemented by the architecture topology projector. This stage documents the contract only; it does not redesign or simplify it.

## Settings

`NodeDuplicationSettings.AllowDuplicateNodes` defaults to `true`.

`DuplicationExceptionPatterns` contains regular expressions. Each pattern is matched against a node's display name and semantic type identity. Invalid regular expressions fail projection as a settings error.

## Projection Rules

- When `AllowDuplicateNodes` is `true`, every visited branch is allowed to create a new physical render instance. These instances have `DuplicationReason.GlobalPolicy`.
- When `AllowDuplicateNodes` is `false`, the first visit to a semantic node creates its canonical physical instance. Later visits reuse that instance.
- A node matching an exception pattern starts a duplicated branch even when global duplication is disabled. Its downstream visits remain branch-local and may also be duplicated. These instances have `DuplicationReason.ExceptionPattern`.
- A canonical physical instance retains the semantic node's identity and source project ownership.
- A duplicated external instance may become owned by the constructing node's project so it can be rendered locally.
- A cycle does not create an unbounded chain. When the current semantic node already exists in the active ancestor map, the link targets the existing ancestor render instance.
- `RenderInstancesBySemanticNodeId` records every physical instance for each semantic node, in discovery order.
- `ArchitectureRenderNode.Order` records physical discovery order. `ArchitectureRenderLink.Order` records projected link discovery order.

## Current Limitations

The Boolean currently combines two concerns: unrestricted branch duplication and the ability to use configured exception patterns. It does not express an independent policy for which node kinds may be duplicated. Any future policy change must introduce an explicit setting or versioned contract rather than changing the meaning of this Boolean silently.
