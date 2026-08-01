@{
    # T5.1 - PSScriptAnalyzer settings for WinCare
    # Ban trailing semicolons that terminate statements — the codebase has pervasive
    # statement-terminating semicolons (e.g. "$a=1;$b=2") that reduce readability and
    # make diff review harder. Enforce clean multi-line statement style.
    #
    # Usage: Invoke-ScriptAnalyzer -Path src/WinCare/Core -Settings tools/PSScriptAnalyzer.psd1
    IncludeRules = @(
        # Semicolon ban — built-in rule, enforced as error
        'PSAvoidSemicolonsAsLineTerminators'

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
        PSAvoidSemicolonsAsLineTerminators = @{
            # WHY: Semicolons used as line or statement terminators compress multiple
            # statements onto lines, making code review, diffing, and debugging harder.
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
