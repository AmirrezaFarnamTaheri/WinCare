#!/usr/bin/env python3
"""Validate WinCare peer-convergence evidence and bidirectional traceability."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ALLOWED_DISPOSITIONS = {
    "adopted", "adapted", "hardened", "extracted", "recomposed",
    "synthesized", "inspired-native", "guardrail-derived", "superseded",
    "rejected-with-reason", "reference-only",
}
ALLOWED_RISKS = {"critical", "high", "medium", "low"}
ALLOWED_CONFIDENCE = {"high", "medium", "low", "unknown"}
ALLOWED_VALIDATION = {"verified", "statically-validated", "reviewed", "inferred", "unverified", "pending"}
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
DONOR_RE = re.compile(r"^D(?:0[1-9]|[1-9][0-9]+)$")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def split_ids(value: str) -> list[str]:
    return [part.strip() for part in value.split(";") if part.strip() and part.strip() != "n/a"]


def split_nodes(value: str) -> list[str]:
    return [part.strip() for part in value.split(";") if part.strip() and part.strip() != "n/a"]


def load_csv(path: Path) -> tuple[list[dict[str, str]], list[str]]:
    with path.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        return list(reader), list(reader.fieldnames or [])


def node_path(node: str) -> str:
    return node.split("#", 1)[0]


def validate(args: argparse.Namespace) -> dict[str, object]:
    root = args.root.resolve()
    evidence = (root / args.evidence_dir).resolve()
    ledger_path = evidence / "adoption-matrix.csv"
    surfaces_path = evidence / "surface-accountability.csv"
    inventory_path = evidence / "donor-inventory.json"

    errors: list[str] = []
    warnings: list[str] = []

    for required in (ledger_path, surfaces_path, inventory_path):
        if not required.is_file():
            errors.append(f"Missing evidence file: {required.relative_to(root)}")
    if errors:
        return {"status": "failed", "errors": errors, "warnings": warnings}

    ledger, ledger_columns = load_csv(ledger_path)
    surfaces, surface_columns = load_csv(surfaces_path)
    inventory = json.loads(inventory_path.read_text(encoding="utf-8"))

    required_ledger_columns = [
        "record_id", "parent_record_id", "composition_group_id", "donor", "domain",
        "value_unit", "source_granularity", "value_form", "separability", "donor_path",
        "donor_sha256", "donor_symbol", "transformation", "mapping_topology", "disposition",
        "decision_rationale", "target_capability", "target_nodes", "invariant",
        "negative_invariant", "test_node", "operator_surface", "migration_impact", "license_note",
        "risk_tier", "dependency_record_ids", "evidence_confidence", "validation_status",
    ]
    required_surface_columns = [
        "donor", "path", "sha256", "size_bytes", "file_type", "language", "classification",
        "authority_status", "semantic_record_ids", "notes",
    ]
    if ledger_columns != required_ledger_columns:
        errors.append("Adoption matrix columns do not match the canonical schema/order.")
    if surface_columns != required_surface_columns:
        errors.append("Surface matrix columns do not match the canonical schema/order.")

    ids = [row.get("record_id", "").strip() for row in ledger]
    id_counts = Counter(ids)
    for record_id, count in sorted(id_counts.items()):
        if not record_id:
            errors.append("Adoption matrix contains an empty record_id.")
        elif count != 1:
            errors.append(f"Duplicate adoption record_id: {record_id}")
    known_ids = set(ids)

    inventory_donors = {str(item.get("donor_id", item.get("donor", ""))).strip() for item in inventory}
    numeric_donors = sorted(int(donor[1:]) for donor in inventory_donors if DONOR_RE.match(donor))
    maximum_donor = max(numeric_donors, default=0)
    expected_donors = {f"D{i:02d}" for i in range(1, maximum_donor + 1)}
    ledger_donors = {row["donor"].strip() for row in ledger}
    surface_donors = {row["donor"].strip() for row in surfaces}
    for name, actual in (("ledger", ledger_donors), ("surface matrix", surface_donors), ("inventory", inventory_donors)):
        if actual != expected_donors:
            errors.append(f"{name} donor coverage mismatch: missing={sorted(expected_donors-actual)} extra={sorted(actual-expected_donors)}")

    parent_graph: dict[str, list[str]] = defaultdict(list)
    dependency_graph: dict[str, list[str]] = defaultdict(list)
    linked_surface_count = Counter()
    test_to_records: dict[str, list[str]] = defaultdict(list)
    target_to_records: dict[str, list[str]] = defaultdict(list)

    for row in ledger:
        record_id = row["record_id"].strip()
        donor = row["donor"].strip()
        for key, value in row.items():
            if value is None or not str(value).strip():
                errors.append(f"{record_id}: empty required column {key}")
        if not DONOR_RE.match(donor):
            errors.append(f"{record_id}: invalid donor identifier {donor!r}")
        if row["disposition"] not in ALLOWED_DISPOSITIONS:
            errors.append(f"{record_id}: invalid disposition {row['disposition']!r}")
        if row["risk_tier"] not in ALLOWED_RISKS:
            errors.append(f"{record_id}: invalid risk tier {row['risk_tier']!r}")
        if row["evidence_confidence"] not in ALLOWED_CONFIDENCE:
            errors.append(f"{record_id}: invalid evidence confidence {row['evidence_confidence']!r}")
        if row["validation_status"] not in ALLOWED_VALIDATION:
            errors.append(f"{record_id}: invalid validation status {row['validation_status']!r}")
        if not SHA256_RE.match(row["donor_sha256"]):
            errors.append(f"{record_id}: invalid donor_sha256")
        parent = row["parent_record_id"].strip()
        if parent != "n/a":
            if parent not in known_ids:
                errors.append(f"{record_id}: unknown parent {parent}")
            parent_graph[record_id].append(parent)
        for dep in split_ids(row["dependency_record_ids"]):
            if dep not in known_ids:
                errors.append(f"{record_id}: unknown dependency {dep}")
            dependency_graph[record_id].append(dep)
        for node in split_nodes(row["target_nodes"]):
            relative = node_path(node)
            path = root / relative
            if not path.is_file():
                errors.append(f"{record_id}: target node path does not exist: {relative}")
            target_to_records[node].append(record_id)
        for test in split_nodes(row["test_node"]):
            relative = node_path(test)
            path = root / relative
            if not path.is_file():
                errors.append(f"{record_id}: test node path does not exist: {relative}")
            test_to_records[test].append(record_id)
        if row["disposition"] not in {"reference-only", "rejected-with-reason"} and row["target_nodes"] == "n/a":
            errors.append(f"{record_id}: implemented/derived disposition lacks target_nodes")
        if row["disposition"] != "reference-only" and row["test_node"] == "n/a":
            errors.append(f"{record_id}: material disposition lacks acceptance evidence")

    def check_acyclic(graph: dict[str, list[str]], label: str) -> None:
        visiting: set[str] = set()
        visited: set[str] = set()
        def visit(node: str, stack: list[str]) -> None:
            if node in visiting:
                cycle = stack[stack.index(node):] + [node]
                errors.append(f"{label} cycle: {' -> '.join(cycle)}")
                return
            if node in visited:
                return
            visiting.add(node)
            stack.append(node)
            for nxt in graph.get(node, []):
                visit(nxt, stack)
            stack.pop()
            visiting.remove(node)
            visited.add(node)
        for node in graph:
            visit(node, [])
    check_acyclic(parent_graph, "Parent")
    check_acyclic(dependency_graph, "Dependency")

    surface_keys: set[tuple[str, str]] = set()
    surface_hashes: dict[tuple[str, str], str] = {}
    donor_counts = Counter()
    classifications = Counter()
    for row in surfaces:
        donor = row["donor"].strip()
        path_value = row["path"].replace("\\", "/").strip()
        key = (donor, path_value.casefold())
        if key in surface_keys:
            errors.append(f"Duplicate/case-colliding donor surface: {donor}:{path_value}")
        surface_keys.add(key)
        donor_counts[donor] += 1
        classifications[row["classification"]] += 1
        try:
            size = int(row["size_bytes"])
            if size < 0:
                raise ValueError
        except ValueError:
            errors.append(f"Invalid size for donor surface {donor}:{path_value}")
        if not SHA256_RE.match(row["sha256"]):
            errors.append(f"Invalid SHA-256 for donor surface {donor}:{path_value}")
        surface_hashes[(donor, path_value)] = row["sha256"]
        record_ids = split_ids(row["semantic_record_ids"])
        if not record_ids:
            errors.append(f"Surface lacks semantic disposition: {donor}:{path_value}")
        for record_id in record_ids:
            if record_id not in known_ids:
                errors.append(f"Surface {donor}:{path_value} references unknown record {record_id}")
            linked_surface_count[record_id] += 1

    for row in ledger:
        record_id = row["record_id"]
        donor_path = row["donor_path"].replace("\\", "/")
        key = (row["donor"], donor_path)
        if key not in surface_hashes:
            errors.append(f"{record_id}: representative donor path is absent from surface matrix: {key[0]}:{key[1]}")
        elif surface_hashes[key] != row["donor_sha256"]:
            errors.append(f"{record_id}: representative donor hash differs from surface matrix")
        if linked_surface_count[record_id] == 0:
            errors.append(f"{record_id}: no donor surface links back to this semantic record")

    inventory_by_donor = {str(item.get("donor_id", item.get("donor"))): item for item in inventory}
    for donor in expected_donors:
        item = inventory_by_donor.get(donor)
        if not item:
            continue
        reported_files = item.get("files") or item.get("file_count") or item.get("member_count")
        if reported_files is not None and int(reported_files) != donor_counts[donor]:
            errors.append(f"{donor}: donor inventory file count {reported_files} != surface matrix {donor_counts[donor]}")
        archive_hash = item.get("archive_sha256") or item.get("sha256")
        if archive_hash and not SHA256_RE.match(str(archive_hash)):
            errors.append(f"{donor}: invalid archive SHA-256 in donor inventory")

    total_inventory_files = sum(int(item.get("files") or item.get("file_count") or item.get("member_count") or 0) for item in inventory)
    if total_inventory_files and total_inventory_files != len(surfaces):
        errors.append(f"Inventory total {total_inventory_files} != surface total {len(surfaces)}")

    composition_groups = Counter(row["composition_group_id"] for row in ledger)
    disposition_counts = Counter(row["disposition"] for row in ledger)
    risk_counts = Counter(row["risk_tier"] for row in ledger)
    license_counts = Counter(row["license_note"] for row in ledger)

    report = {
        "schema": "wincare.convergence.validation/v1",
        "root": ".",
        "evidence": {
            "adoptionMatrix": str(ledger_path.relative_to(root)),
            "adoptionMatrixSha256": sha256_file(ledger_path),
            "surfaceMatrix": str(surfaces_path.relative_to(root)),
            "surfaceMatrixSha256": sha256_file(surfaces_path),
            "donorInventory": str(inventory_path.relative_to(root)),
            "donorInventorySha256": sha256_file(inventory_path),
        },
        "counts": {
            "donors": len(expected_donors),
            "semanticRecords": len(ledger),
            "classifiedSurfaces": len(surfaces),
            "compositionGroups": len(composition_groups),
            "targetNodes": len(target_to_records),
            "testNodes": len(test_to_records),
        },
        "dispositions": dict(sorted(disposition_counts.items())),
        "risks": dict(sorted(risk_counts.items())),
        "classifications": dict(sorted(classifications.items())),
        "licenses": dict(sorted(license_counts.items())),
        "donorSurfaceCounts": dict(sorted(donor_counts.items())),
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
    }
    report["status"] = "passed" if not report["errors"] else "failed"
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--evidence-dir", default="docs/convergence")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = validate(args)
    payload = json.dumps(report, indent=2, sort_keys=False)
    print(payload)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(payload + "\n", encoding="utf-8")
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
