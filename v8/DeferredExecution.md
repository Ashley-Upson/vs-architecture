# Deferred execution view

Enable `Architecture.DeferredExecutionSupport` through the existing JSON render
configuration parameter. It defaults to false. `Architecture.DeferredExecution.json`
is an example with coloured connections and inline external components enabled.

## Extraction

For a call inside a callback supplied as an argument:

1. Find the receiving method and delegate parameter using Roslyn symbols.
2. Follow that parameter through source methods and internal interface implementations.
3. Track local aliases and callbacks stored in fields/properties of the receiving type.
4. Stop when the delegate is invoked, or when the receiving method belongs to an
   external assembly. External interfaces are boundaries even if a local wrapper
   implements that interface; otherwise a wrapper forwarding to its own interface
   would create an inferred cycle.
5. Record `ExecutorType` and `RegistrationType` on the callback's usage relationship.
   `RegistrationType` identifies the last source component handing over the callback.
6. Retain constructor-supplied dependency usage through `IsInjected` when that same
   dependency is captured by a callback.

Immediate wrappers on the registering type, such as its own `TryCatch`, retain
ordinary usage. Forwarding cycles and untraceable expressions retain ordinary
usage too. This is source-based tracing, not proof that an event will occur at
runtime. An external receiver is an execution boundary; its scheduling behaviour
is not inferred from its name or from the fact that it accepts a delegate.

The trace follows the source available in the project compilation. It does not
inspect external method bodies. Arbitrary storage containers, reflection, and
callbacks whose argument flow cannot be established are not guessed.

## Presentation

`DeferredExecutionRenderModelProcessingService` is DI-registered and operates on
the prepared `RenderModel` before layout. When the option is enabled:

- Callback-only usage links from the registering type are removed. Ordinary calls
  and captured constructor dependencies remain.
- A registration occurrence of the communication component is placed at each
  receiving end of the forwarding graph.
- An execution occurrence is placed above each affected handler occurrence, with
  a dashed connection to that handler. Handler descendants use the existing layout.
- Matching `bus N` labels connect the occurrences conceptually, without a line
  running from the bottom of the diagram back to its top.

These occurrences have different node IDs and the same underlying type identity.
They remain separate even with `NoDuplicates` enabled. Layout uses occurrence
identity for these nodes; it does not merge them by type name. The raw project
models are not mutated and disabling the option retains the previous view.

Both HTML and Draw.io consume these same prepared occurrences and routes.
