@{
    # T5.1 - PSScriptAnalyzer settings for WinCare
    # Ban semicolon-chained statements — the codebase has pervasive one-line
    # minification (e.g. "if($x){$a=1};$b=2") that reduces readability and
    # makes diff review harder. Enforce multi-line statement style in Core first.
    #
    # Usage: Invoke-ScriptAnalyzer -Path src/WinCare/Core -Settings tools/PSScriptAnalyzer.psd1
    IncludeRules = @(
        # Semicolon ban — new rule, enforced as error
        'PSAvoidSemicolonsAsStatementSeparators'

        # Standard rules enabled as warnings (pre-existing; not newly breaking)
        'PSAvoidUsingWriteHost'
        'PSAvoidUsingInvokeExpression'
        'PSAvoidUsingPlainTextForPassword'
        'PSAvoidUsingConvertToSecureStringWithPlainText'
        'PSAvoidGlobalVars'
        'PSAvoidUsingCmdletAliases'
        'PSMissingModuleManifestField'
        'PSUseSingularNouns'
        'PSUseApprovedVerbs'
        'PSUseDeclaredVarsMoreThanAssignments'
        'PSUseShouldProcessForStateChangingFunctions'
        'PSAvoidTrailingWhitespace'
        'PSUseConsistentIndentation'
        'PSUseConsistentWhitespace'
    )

    Rules = @{
        PSAvoidSemicolonsAsStatementSeparators = @{
            # WHY: Semicolon-chaining compresses multiple statements onto one line,
            # making code review, diffing, and debugging harder. This is the primary
            # readability issue identified in the T5.1 coding standards audit.
            Enable   = $true
            Severity = 'Error'
        }
        PSAvoidUsingWriteHost = @{
            Enable   = $true
            Severity = 'Warning'
        }
        PSUseConsistentIndentation = @{
            Enable              = $true
            IndentationSize     = 4
            PipelineIndentation = 'IncreaseIndentationForFirstPipeline'
            Kind                = 'space'
            Severity            = 'Information'
        }
        PSUseConsistentWhitespace = @{
            Enable                          = $true
            CheckInnerBrace                 = $true
            CheckOpenBrace                  = $true
            CheckOpenParen                  = $true
            CheckOperator                   = $true
            CheckPipe                       = $true
            CheckPipeForRedundantWhitespace = $false
            CheckSeparator                  = $true
            CheckParameter                  = $false
            Severity                        = 'Information'
        }
    }
}
