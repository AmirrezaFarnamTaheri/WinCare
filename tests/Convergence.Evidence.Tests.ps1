
Describe 'Peer convergence evidence' {
    #requires -Version 7.2
BeforeAll {
    $script:Root=Split-Path $PSScriptRoot -Parent
    $script:Ledger=Import-Csv (Join-Path $script:Root 'docs\convergence\adoption-matrix.csv')
    $script:Surfaces=Import-Csv (Join-Path $script:Root 'docs\convergence\surface-accountability.csv')
    $script:Inventory=Get-Content (Join-Path $script:Root 'docs\convergence\donor-inventory.json') -Raw|ConvertFrom-Json
}
    It 'accounts for every supplied donor and surface' {
        @($script:Inventory).Count|Should Be 166
        @($script:Surfaces).Count|Should Be 52629
        @($script:Ledger).Count|Should BeGreaterThan 175
        @($script:Surfaces|Where-Object{[string]::IsNullOrWhiteSpace($_.semantic_record_ids)}).Count|Should Be 0
        @($script:Ledger.donor|Sort-Object -Unique)|Should Be 166
    }
    It 'uses unique stable record IDs and valid dispositions' {
        @($script:Ledger.record_id|Sort-Object -Unique).Count|Should Be @($script:Ledger).Count
        $allowed=@('adopted','adapted','hardened','extracted','recomposed','synthesized','inspired-native','guardrail-derived','superseded','rejected-with-reason','reference-only')
        foreach($row in $script:Ledger){$row.record_id|Should Not BeNullOrEmpty;$row.disposition|Should -BeIn $allowed;$row.donor_sha256|Should Match '^[0-9a-f]{64}$'}
    }
    It 'resolves every target and test path' {
        foreach($row in $script:Ledger){
            foreach($node in @($row.target_nodes -split ';'|Where-Object{$_ -and $_ -ne 'n/a'})){Test-Path -LiteralPath (Join-Path $script:Root ($node -split '#')[0])|Should Be $true}
            foreach($node in @($row.test_node -split ';'|Where-Object{$_ -and $_ -ne 'n/a'})){Test-Path -LiteralPath (Join-Path $script:Root ($node -split '#')[0])|Should Be $true}
        }
    }
    It 'links every semantic record back from at least one donor surface' {
        $linked=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($surface in $script:Surfaces){foreach($id in @($surface.semantic_record_ids -split ';')){$null=$linked.Add($id)}}
        foreach($row in $script:Ledger){$linked.Contains($row.record_id)|Should Be $true}
    }
    It 'has no evidence validation errors' {
        $validation=Get-Content (Join-Path $script:Root 'docs\convergence\convergence-validation.json') -Raw|ConvertFrom-Json
        $validation.status|Should Be 'passed'
        @($validation.errors).Count|Should Be 0
    }
}
