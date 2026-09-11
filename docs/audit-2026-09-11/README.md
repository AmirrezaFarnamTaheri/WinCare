# WinCare audit — finalized document package

**Document status:** finalized, incorporating the forensic follow-up. **Verification status:** incomplete. Finalizing these documents does not close the outstanding experiments or make WinCare release-ready.

Reviewed source: `b30516a5bb6fdd13e844a6022906dd03090b576e`. The installed rc5 executable is a separate evidence artifact whose exact source/build provenance remains unresolved.

## Start here

- [Authoritative report](D:/GitHub/WinCare/docs/audit-2026-09-11/report.md): sections A–AF, findings, assessments and remediation roadmap.
- [Forensic follow-up](D:/GitHub/WinCare/docs/audit-2026-09-11/forensic-follow-up.md): accepted omissions, corrected critique claims and outstanding validation.
- [Coverage ledger](D:/GitHub/WinCare/docs/audit-2026-09-11/coverage-ledger.md): inspection depth, empirical coverage and gaps.
- [Evidence index](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence-index.md): retained experiments and their limitations.
- [Answers to the 47 requested questions](D:/GitHub/WinCare/docs/audit-2026-09-11/questions-answered.md).

## Reconciled totals and conclusions

| Measure | Final documented result |
|---|---|
| Findings | 40: 13 High, 25 Medium, 2 Low; none classified Critical |
| Installed crashing destinations | 8 = 7 collection-projection failures + 1 missing-converter failure |
| Catalog commands | 259 = 155 read-only + 104 mutating |
| Individually executed commands | 11 read-only; 0 mutating |
| Individually unexecuted commands | 248 = 144 read-only + 104 mutating |
| Shared-export evidence | 9 source-traced callers of a runtime-tested failing helper; not 9 individually tested failures |
| Mutating catalog risks | 1 Critical, 17 High, 73 Moderate, 13 Low; separate from finding severity and effective dispatcher policy |
| Performance conclusion | Completed 60-second warm Home sample did not reproduce sustained idle CPU load; cold/active/GPU/long-run behavior remains unverified |
| Product verdict | Not release-ready |

The initial trim/no-trim comparison was invalid because its outputs were byte-identical. The later strict restored build failed with IL2026 errors. Neither establishes the exact cause of the installed collection crashes. `PageRow` is public. Guard has an explicit pipe ACL; no LPE was demonstrated. Intended caching and collectible plugin contexts do not prove either leaks or successful collection without lifetime tests.

## Document ownership and regeneration

| File | Ownership |
|---|---|
| `build_report.py` | Authored finding register and report generator |
| `report-introduction.md`, `report-assessments.md` | Authored report sections; edit these rather than generated report.md |
| `report.md`, `findings.json` | Generated from the above |
| `inventory.py` | Source inventory generator; running it resets inventory annotations |
| `command-matrix.csv` | Generated inventory, then annotated by build_report.py with evidence classes |
| `command-coverage-summary.json` | Generated coverage counts |
| `coverage-ledger.md`, `questions-answered.md`, `forensic-follow-up.md`, `evidence-index.md`, `README.md` | Authored companions, checked for consistency |
| `evidence/` | Historical evidence; do not overwrite to make failed experiments appear successful |
| `harness/`, `*-probe.ps1` | Diagnostic experiment tools; documentation regeneration does not execute them |

From the repository root, regenerate with `python docs/audit-2026-09-11/build_report.py`, then check with `python docs/audit-2026-09-11/validate_documents.py`. If intentionally rebuilding the source inventory, run inventory.py first and build_report.py afterward to restore evidence annotations. Regenerating against a different commit requires a new provenance review; it must not silently reuse this audit's runtime evidence as proof for changed source.

The [earlier 32-item review](D:/GitHub/WinCare/docs/review-2026-09-11.md) is superseded historical material, not a competing current register. Its numbering is not interchangeable with F-001–F-040. The forensic follow-up adds coverage qualifications, not silently verified new findings.

No production implementation was changed as part of this audit or document finalization. Remaining work is specified in report sections AC–AE and the forensic follow-up.
