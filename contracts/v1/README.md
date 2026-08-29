# TransDuck contract v1

`contracts/v1/` is the cross-platform source of truth for the first public
contract major. The macOS Vapor `TranslationRequest` and `DictionaryEntry`
types are legacy inputs only; they do not define the v1 wire format.

## Compatibility

- Every document uses JSON camelCase property names and `schemaVersion: 1`.
- Enum values are strings. Missing required properties, a non-`1` schema
  version, an unknown enum value, or an invalid terminal shape are invalid.
- Consumers must ignore unknown properties so v1 may add optional fields.
- Optional `provider.instanceId` and `result.dictionaryEntries` may be omitted
  or explicitly set to `null` by compatible decoders. Canonical encoders omit
  their null values. This does not relax terminal `result`, `error`, or
  `text` shapes: explicitly null terminal fields remain invalid.
- A published v1 field's meaning, type, and requiredness never change in
  place. A v1 addition must be optional; a breaking change requires a new
  major directory.
- `stream-event` sequence values are non-negative and terminal events are
  shaped by their `eventType`: `completed` and `cancelled` carry neither text
  nor error, while `failed` carries an error.

## Scope and safety

The schemas intentionally exclude provider endpoints, API keys, device IDs,
and credential storage. Fixtures use only synthetic request and history text.
Runtime credentials, history persistence, and logging are separate concerns.

Schemas use JSON Schema Draft 2020-12. The Core implementation mirrors their
required fields and terminal invariants with `System.Text.Json` and BCL-only
validation; it deliberately does not require a runtime JSON Schema package.

## Fixture matrix

Both the C# and Swift contract suites must read
[`fixtures/manifest.json`](fixtures/manifest.json) instead of hard-coding a
fixture list. Its entries are sorted by relative path, each path is relative to
`fixtures/`, and every fixture file appears exactly once. Invalid entries carry
the stable camelCase `errorCategory` expected from contract validation; valid
entries omit that field. Consumers must reject duplicate, missing, or
out-of-tree paths before loading fixture content.
