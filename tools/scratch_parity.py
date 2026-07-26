import re
from pathlib import Path
contract = Path('src/WinCare/Core/11-ActionContracts.ps1').read_text('utf-8')
transaction = Path('src/WinCare/Core/09-Transactions.ps1').read_text('utf-8')
contracts = set(re.findall(r"Add-Contract\s+'([^']+)'", contract))
dispatch = set(re.findall(r"(?m)^\s*'([^']+)'\s*\{Invoke-WinCare", transaction))
print('contracts:', len(contracts))
print('dispatch:', len(dispatch))
print('missing from dispatch (contract defined but no dispatch case):', sorted(contracts - dispatch))
print('missing from contracts (dispatch case but no contract):', sorted(dispatch - contracts))
