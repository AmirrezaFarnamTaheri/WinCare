function Get-WinCareCanonicalPathHash {
    param([Parameter(Mandatory)][string]$Path)
    $canonical=[IO.Path]::GetFullPath($Path).TrimEnd('\').ToLowerInvariant();$bytes=[Text.Encoding]::UTF8.GetBytes($canonical)
    try{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}finally{[Array]::Clear($bytes,0,$bytes.Length)}
}
function Get-WinCareParentPath {
    param([Parameter(Mandatory)][string]$Path)
    $parent=[IO.Directory]::GetParent([IO.Path]::GetFullPath($Path))
    if($null -eq $parent){throw "Path has no parent directory: $Path"}
    $parent.FullName
}
function Assert-WinCareManagedPath {
    param([Parameter(Mandatory)][string]$Path)
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\');$root=[IO.Path]::GetPathRoot($full).TrimEnd('\')
    $blocked=@($root,$env:SystemRoot,$env:ProgramFiles,${env:ProgramFiles(x86)},$env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA)|Where-Object{$_}|ForEach-Object{[IO.Path]::GetFullPath($_).TrimEnd('\')}
    if($full -in $blocked){throw "Refusing unsafe WinCare path: $full"}
    $cursor=$full
    while($cursor -and -not(Test-Path -LiteralPath $cursor)){
        $parent=[IO.Directory]::GetParent($cursor)
        if($null -eq $parent -or $parent.FullName -eq $cursor){break}
        $cursor=$parent.FullName
    }
    while($cursor -and (Test-Path -LiteralPath $cursor)){
        $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "WinCare path traverses a reparse point: $cursor"}
        $parent=[IO.Directory]::GetParent($cursor)
        if($null -eq $parent -or $parent.FullName -eq $cursor){break}
        $cursor=$parent.FullName
    }
    $full
}
function Remove-WinCareTree {
    param([Parameter(Mandatory)][string]$Root)
    $rootPath=Assert-WinCareManagedPath $Root;if(!(Test-Path -LiteralPath $rootPath)){return};$rootItem=Get-Item -LiteralPath $rootPath -Force -ErrorAction Stop;if(!$rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe deletion root.'}
    $stack=[Collections.Generic.Stack[object]]::new();$stack.Push([pscustomobject]@{P=$rootPath;V=$false});$count=0
    while($stack.Count){$frame=$stack.Pop();if($frame.V){[IO.Directory]::Delete($frame.P,$false);continue};$stack.Push([pscustomobject]@{P=$frame.P;V=$true});foreach($child in Get-ChildItem -LiteralPath $frame.P -Force -ErrorAction Stop){$count++;if($count -gt 30000){throw 'Deletion entry ceiling exceeded.'};if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){if($child.PSIsContainer){[IO.Directory]::Delete($child.FullName,$false)}else{[IO.File]::Delete($child.FullName)}}elseif($child.PSIsContainer){$stack.Push([pscustomobject]@{P=$child.FullName;V=$false})}else{[IO.File]::SetAttributes($child.FullName,[IO.FileAttributes]::Normal);[IO.File]::Delete($child.FullName)}}}
}
