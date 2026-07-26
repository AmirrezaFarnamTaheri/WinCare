# Source-reference model

WinCare action, plan, catalog, knowledge, and operator records retain the durable `SourceRecords` field for schema compatibility. Its values now identify **current shipped implementation ownership**, not superseded planning labels or external-equivalence claims.

- `SRC:<digest>` is a stable identifier derived from the authoritative relative source path.
- `src/WinCare/Data/SourceReferenceIndex.json` maps each identifier to the exact shipped path and its source SHA-256 at the time the index is generated.
- The index is release input and is covered by the package manifest and SBOM.
- Third-party licensing and release provenance remain separate in `THIRD-PARTY-NOTICES.md`, Git history, the generated SBOM, and the build receipt.
- Generated GUI command coverage may have no source reference; its authority is the public command contract.

New code must not add numbered historical IDs. Source references must resolve through the current index.
