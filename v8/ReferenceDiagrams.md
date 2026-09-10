# Hand-built diagram references

Reviewed source folder: `C:\Data\Documentation\Corporate LinX\Diagrams`.
The examples establish the desired visual direction. They are reference material,
not instructions to modify the source diagrams or evidence that depicted annotations
can all be inferred from Roslyn.

## Most relevant pages

- `Core Architecture.drawio`, **Security Service Model**: compact vertical service
  stacks aligned across a logical SSO scope, with related App components alongside.
  Reviewed the XML and a Draw.io PNG export. 64 vertices and 45 edges, including legends
  and annotations; these counts are not counts of implementation classes.
- `Infrastructure.drawio`, **Code Stack**: a particularly useful generic orchestration
  splitting into storage and eventing stacks, plus larger invoice stacks and explicit
  inheritance examples. Reviewed the XML and a Draw.io PNG export.
- `CLX B2B Architecture.drawio`, **Transactions (condensed)**: repeated transaction
  service groups, side-by-side storage/event paths and shared dependencies. Reviewed
  the XML geometry, labels and styles. 174 vertices and 189 edges.

The folder also contains infrastructure, entity, workflow/process and UI review
material. Those are different views; they should not all become requirements for
one architecture renderer.

## Observed visual conventions

| Feature | Reference examples | Current v8 output |
| --- | --- | --- |
| Labels | Short names, often wrapped onto two or three lines | Full namespace-qualified names |
| Node sizes | Many service boxes are 120 by 60 units | Width expands with full-name length |
| Colours | Architectural role | Class/interface/external status |
| Rows | Controller, orchestration, processing, foundation, broker | Breadth-first dependency distance |
| Columns | Related vertical service chains; event chain alongside | Input order within each depth |
| Connections | Mostly unlabelled type dependencies | Separate labelled method calls |
| Scope | Named logical systems/entities, sometimes multiple stacks per scope | One project-labelled box per splitter result |
| Shared endpoints | Databases and hubs below stacks, sometimes shared | Every reached type shown as a rectangle per tree |

Observed role fill colours include controller blue `#0050ef`, orchestration cyan
`#1ba1e2`, processing purple `#6a00ff`, foundation green `#008a00`, broker light green
`#60a917`, and scope charcoal `#313a42`. Most role boxes explicitly use white text.
These colours are visual references, not a requirement to copy low-contrast pairs;
our generated palette should keep text readable in both editor themes.

Dependencies often use straight centre-aligned vertical links, with horizontal
segments in the space between rows. Some event links deliberately go upwards or
sideways. The examples therefore support a preferred downward flow, not a universal
rule that every edge must descend. Inheritance is deliberately shown in some pages;
hiding every Inheritance edge would lose that intent.

## Proposed next increment

Preserve ProjectModel as the extracted facts. Before placement, build a separate
internal display model:

1. Choose visible types. Usually show concrete service implementations; omit a
   redundant interface node when its concrete dependencies already represent it.
   Retain meaningful unresolved contracts and intentional base-class relationships.
2. Produce short display labels, keeping full names as identity/metadata and using
   namespace qualification only where short names collide.
3. Consolidate method dependencies into one displayed connection per directed type
   pair and relationship kind. Retain the contributing method calls as details.
4. Assign architectural roles from explicit naming/namespace conventions or supplied
   classification rules. Roslyn symbols alone do not label a type as a processing
   service. Keep an unknown role rather than guessing unsupported architecture.
5. Use roles to align rows and dependencies to arrange related columns. Keep the
   storage and eventing chains next to each other. Preserve skipped levels as gaps
   rather than moving foundations into processing rows.
6. Size compact, wrapped boxes, apply contrasting role colours, then route connections
   through row gutters. These are layout rules independent of model extraction.

The first concrete acceptance target should be SchoolManager above its orchestration,
then the two parallel school processing/foundation/broker chains, with SchoolFactory
and IEventHub represented according to explicit boundary rules. It should resemble
the generic Code Stack example while remaining traceable to the sample's actual code.

Keep the current user-approved repetition of shared dependencies between splitter
outputs. Merging all scopes or imposing globally unique nodes is a separate decision;
the references do not silently override that policy. Their logical entity grouping
also should not be assumed to match physical .csproj boundaries.

## Facts still missing for other reference features

- Database/storage resource identity and deployment topology are not encoded by our
  current type/method dependency model.
- Event publisher-to-subscriber routes need event names, payloads and registration
  analysis; calling IEventHub alone does not establish the delivery graph.
- The model currently omits constructor creation edges, including SchoolFactory to
  SchoolDataContext. Do not invent that link solely to match the drawing.
- Implementation/test/security/compliance ticks require separate evidence. A public
  method or a test project's existence is not proof of those statuses.

This review changes no extraction, splitting or rendering behaviour. It documents
what the references imply for the next drawing increment.
