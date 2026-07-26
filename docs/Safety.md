# Safety Model

## Defaults

- queries are read-only;
- mutations are preview-first;
- high-impact operations require explicit approval;
- unsupported prerequisites fail closed;
- irreversible work discloses the blast radius and absence of recovery;
- read-only mode performs no persistent initialization, repair, cache, or log write.

## Resource safety

Files, archives, directory walks, process output, HTTP bodies, IPC frames, JSON, canonical objects, logs, and durable stores have explicit entry, byte, depth, line, time, and aggregate ceilings. Reparse traversal, path escape, alternate data streams, archive collisions, unsafe links, and change-during-use are rejected.

## Recovery

Reversible actions capture exact prior state and an executable compensator before mutation. Atomic write helpers verify committed hashes and preserve recovery artifacts when rollback cannot be proven. Interrupted operations are reconciled only in persistent non-read-only sessions.

## Dependency safety

Optional tools, models, programs, and runtimes are admitted through regular-file checks, path confinement, size bounds, signatures where applicable, and SHA-256 verification immediately before use. Their absence produces an explicit blocked result.

## High-risk exclusions

WinCare does not execute imported shell bodies, arbitrary registry scripts, untrusted extensions, packet-evasion logic, covert interception, unsigned-driver installation, or hidden background telemetry.
