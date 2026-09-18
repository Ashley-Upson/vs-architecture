# Initial construction

[Catalogue](readme.md)

Architecture projection first creates nodes and connections. LayoutInitializationService establishes depth/category rows, seeds trees horizontally, and aligns shared-parent groups. It runs before the convergence loop. The initial shared-parent adjustment is not replayed after branch packing.

Composition, CallChain and DataModel use their existing contextual builders to construct their initial geometry. They now finish through the same convergence engine. Their initial layout algorithms are not being claimed as independently decomposed iterative rules: only the registered conditions in the catalogue repeat. Their remaining arrangement decisions are documented on the implementation pages.

A caller supplying an uninitialized architecture RenderModel directly receives initialization at the layout-service boundary. A prebuilt contextual model is marked initialized by its orchestration.
