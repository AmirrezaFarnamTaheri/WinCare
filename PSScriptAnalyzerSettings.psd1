@{
    Severity = @('Error','Warning')
    IncludeRules = @(
        'PSAvoidSemicolonsAsLineTerminators',
        'PSAvoidUsingInvokeExpression',
        'PSAvoidUsingPlainTextForPassword',
        'PSAvoidUsingConvertToSecureStringWithPlainText',
        'PSAvoidUsingUsernameAndPasswordParams',
        'PSAvoidUsingComputerNameHardcoded',
        'PSAvoidUsingDeprecatedManifestFields',
        'PSAvoidUsingEmptyCatchBlock'
    )
    Rules = @{
        PSAvoidSemicolonsAsLineTerminators = @{ Enable = $true; Severity = 'Error' }
        PSAvoidUsingPlainTextForPassword = @{ Enable = $true }
        PSAvoidUsingConvertToSecureStringWithPlainText = @{ Enable = $true }
    }
}

