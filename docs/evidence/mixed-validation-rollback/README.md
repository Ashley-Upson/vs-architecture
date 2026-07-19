# Mixed-validation rollback

Component/link:
`tree_type_7aef8d0195c7d2e7__type_7aef8d0195c7d2e7_0_type_dc6bc4acaeb3a7dc_1_type_0142b617a7411bfd__edge_b4e4fdcf63891533`.

The component contains one proposed common-owned link. Mixed validation rejects it atomically. Its compiled common
path is itself invalid: it crosses six node interiors before boundary comparison. At the common/legacy boundary it
also shares a non-zero segment with link `...edge_b6b9355e4cfbb489`, creates a non-clean perpendicular contact with
that link, and violates parallel spacing against links ending `7f0e348053f5b736`, `8ecb200cc065b668`, and
`601a61b30456740f`.

This is not stale legacy geometry and not a validator ownership error. The smallest missing capability is an
obstacle-aware destination-column movement proposal whose complete positional and interaction closure can be
regenerated. An exemption would conceal an invalid common path, so the precise disposition remains
`UnsupportedObstacleMovement`/atomic rollback.

The before/after files retain the complete surrounding diagram because the relevant contacts span the mixed
component boundary. The rejected component is unchanged in `after.drawio`; other independently closed components
show the accepted trial geometry.
