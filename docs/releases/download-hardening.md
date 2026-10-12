# Native Downloads hardening after v4.0.0-rc2

The published rc2 packages are unchanged and retain the limitations described in their release notes. These changes apply to subsequent builds containing this patch.

## URL privacy

Queue records retain a display origin (scheme, host and port) and a Windows current-user DPAPI encrypted transfer URL. The encrypted URL preserves signed queries and fragments for restart/retry; queue command responses omit the ciphertext. Completion and successful cancellation remove the private URL. Transfer failures report an exception type rather than a potentially secret-bearing HTTP error message. Origin metadata deliberately omits HostUrl and ReferrerUrl.

Legacy plaintext queue links are encrypted and replaced on the first queue read or mutation. This migration does not erase copies in backups, old temporary files, journals produced by older versions, or filesystem recovery data. DPAPI protects data at rest from other account contexts; it does not protect against code executing as the same Windows user. Moving a portable data directory to another account or computer may make unfinished links unreadable; add those jobs again. There is no plaintext fallback.

## Ownership and recovery

Queue collision detection, ID uniqueness and append occur in one state transaction. State writers use a bounded exclusive file handle, which the OS releases after process termination; a named semaphore could otherwise remain exhausted after a crash. Complete JSON is flushed to disk before replacement.

Each destination has a durable `.wincare.lock` reservation identifying the queue root and job. An exclusive handle is held throughout transfer, hashing, origin marking and publication. Cancel, remove and reconcile must acquire the same handle before modifying files or state. Another process's active job is blocked; pause/cancel must be requested in the owning process. Reservation files are deliberately retained rather than unlinked, preventing a second lock object from appearing while an old owner still holds a handle. Removing a record clears its reservation. Completed files remain untouched.

A process terminated between reservation creation and queue commit can leave an orphan reservation. This fails closed: do not delete/reset a reservation while any WinCare process may be active. Close all instances and inspect the matching queue records and partial/completed files before manually removing an orphan lock. These are coordinated WinCare operations, not protection against another application or a same-user attacker changing paths. Reparse-point paths, alternate data streams and ambiguous/device filenames are rejected.

Before rename, the queue durably records `Publishing`, the verified digest and byte count. Recovery checks the destination digest and writes/verifies origin metadata before declaring completion. A crash before rename retains the partial; one after rename leaves digest evidence for reconciliation. Missing or mismatched evidence never authorizes overwriting an existing file. Cancellation after the publication decision cannot turn a completed file into a cancelled job.

Resume keeps the existing safety rule: append only when an expected SHA-256 is supplied and HTTP returns the exact requested identity-encoded range. Otherwise retry restarts the transfer. A successful resume still verifies the complete file against that checksum. Pause retains partial bytes; cancellation removes them before reporting a terminal cancelled state.

## Windows file origin

Publication requires writing and reading back `Zone.Identifier` with `ZoneId=3` on the partial file before renaming it. Failure (including a destination filesystem without alternate data streams) retains the partial and reports failure instead of silently publishing an unmarked file. Reconciliation also verifies origin metadata. Mark-of-the-Web is origin metadata, not malware scanning, signature verification, or a guarantee of a particular Windows warning. Copying a file to a filesystem that does not preserve streams can remove it.

## Validation and release status

Focused Windows regression tests cover signed-link restart, legacy migration, corrupted DPAPI ciphertext, concurrent destination creation, independent-process exclusion and crash release, publication recovery, file-origin marking and secret-free transfer errors. Existing native workspace tests cover successful publication and rejected resume ranges. Local source/wiring checks passed; Windows execution and x64/ARM64 build results must be checked on the associated PR workflow before shipping.

Development self-signing remains development self-signing. This work does not supply a production-trusted signing identity and does not claim hands-on Narrator, keyboard, High Contrast, text scaling, installation, upgrade or destructive-operation acceptance. The release remains a prerelease; automated checks do not constitute production certification.
