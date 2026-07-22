# WinCare 4.6 wave-seven target baseline

- Baseline source: clean WinCare 4.5 commit `aada1942d0a02072d88d396a959e999a50d21d57`.
- Working target: additive WinCare 4.6 branch `wave7-convergence`.
- Donor scope: D106-D109 only; cumulative D01-D105 evidence retained unchanged.
- Baseline source gate before edits: 135 PowerShell files, 830 functions, 106 typed action contracts, 170 headless commands, and 46 routed TUI actions.
- Baseline convergence gate before edits: 105 donors, 20,829 surfaces, 414 semantic records, 235 composition groups, 257 target nodes, and 142 test nodes.
- Preservation decision: the clean 4.5 commit was cloned; no prior release artifact or unrelated working-tree change was overwritten.
- Environment: Linux build host without `pwsh`; Windows-native behavior remains a separate release gate.
