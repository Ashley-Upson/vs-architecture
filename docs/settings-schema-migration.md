# Settings schema migration

Persisted diagram settings carry a numeric `version`. `SettingsSchemaVersion.Current` is the version written by the current Core serializer.

Import follows these rules:

1. A settings object without `version` is legacy version 1.
2. Version 1 is deserialized with the user's existing values, normalized into the current model, and assigned the current version.
3. Fields absent from an older document receive their current model defaults.
4. Unknown fields continue to follow `System.Text.Json`'s existing policy and are ignored.
5. A version newer than the current implementation is rejected rather than guessed at.
6. Export always writes the current version.

Later routing migrations should add a deliberate version-to-version step before removing or changing a persisted field. Old numeric values must not be reused for a different unit or meaning. A retired field may be read and ignored for one migration version, but its replacement and default must be explicit and covered by tests.

The Visual Studio settings store requires no deletion or extension reinstall. Existing unversioned/version-1 JSON is migrated when loaded and is written in the current schema on the next save/export.
