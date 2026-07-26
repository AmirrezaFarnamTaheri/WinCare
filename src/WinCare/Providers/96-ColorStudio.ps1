function Get-WinCareColorStudioRoot { Ensure-WinCareState; Get-WinCareBridgeStateRoot 'ColorStudio' }

function Get-WinCareColorPalettePath {Join-Path (Get-WinCareColorStudioRoot) 'palette.json'}

function Get-WinCareDefaultColorPaletteStore {[ordered]@{SchemaVersion=1;Revision=0;UpdatedAt='1970-01-01T00:00:00.0000000Z';Entries=@()}}

function Test-WinCareColorPaletteStore {
    param([Parameter(Mandatory)][Collections.IDictionary]$Store)
    $null=Test-WinCareStrictObjectKeys -InputObject $Store -AllowedKeys @('SchemaVersion','Revision','UpdatedAt','Entries') -Context 'color palette store'
    if([int]$Store.SchemaVersion -ne 1 -or [long]$Store.Revision -lt 0){throw 'Color palette store schema or revision is invalid.'}
    $entries=@($Store.Entries);if($entries.Count -gt 512){throw 'Color palette exceeds 512 entries.'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $entries){
        $null=Test-WinCareStrictObjectKeys -InputObject $entry -AllowedKeys @('Id','Name','Hex','Red','Green','Blue','Alpha','CreatedAt','Source','Tags') -Context 'color palette entry'
        if([string]$entry.Id -notmatch '^[a-f0-9]{32}$' -or -not $ids.Add([string]$entry.Id)){throw 'Color palette entry identity is invalid or duplicated.'}
        if([string]$entry.Name -and ([string]$entry.Name).Length -gt 120){throw 'Color palette entry name is too long.'}
        $created=[datetime]::MinValue;if(-not[datetime]::TryParse([string]$entry.CreatedAt,[ref]$created)){throw 'Color palette creation time is invalid.'}
        if(([string]$entry.Source).Length -gt 240){throw 'Color palette source is too long.'}
        if([string]$entry.Hex -notmatch '^#[A-F0-9]{6}([A-F0-9]{2})?$'){throw 'Color palette entry HEX value is invalid.'}
        foreach($field in @('Red','Green','Blue','Alpha')){if([int]$entry[$field] -lt 0 -or [int]$entry[$field] -gt 255){throw "Color palette $field is outside 0..255."}}
        if(@($entry.Tags).Count -gt 32 -or @($entry.Tags|Where-Object{$_ -isnot [string] -or ([string]$_).Length -gt 64}).Count){throw 'Color palette tags are invalid.'}
    }
    $true
}

function Get-WinCareColorPaletteStore {
    [CmdletBinding()]param([switch]$Strict)
    $path=Get-WinCareColorPalettePath
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return Get-WinCareDefaultColorPaletteStore}
    try{$store=Read-WinCareProtectedJson -LiteralPath $path -Purpose 'WinCare.ColorPalette';$null=Test-WinCareColorPaletteStore -Store $store;return $store}catch{if($Strict){throw};Write-WinCareLog -Level Warning -Message 'Color palette is invalid; returning an empty in-memory view.' -Data @{error=$_.Exception.Message};return Get-WinCareDefaultColorPaletteStore}
}

function Get-WinCareColorPaletteStoreHash {param([Collections.IDictionary]$Store);Get-WinCareCanonicalObjectHash -InputObject $Store}

function Get-WinCareColorPalette {
    [CmdletBinding()]param([string]$Id)
    $entries=@((Get-WinCareColorPaletteStore).Entries);if($Id){$entries=@($entries|Where-Object Id -eq $Id)};@($entries)
}

function ConvertTo-WinCareColorRecord {
    [CmdletBinding()]param([ValidateRange(0,255)][int]$Red,[ValidateRange(0,255)][int]$Green,[ValidateRange(0,255)][int]$Blue,[ValidateRange(0,255)][int]$Alpha=255,[string]$Name='',[string]$Source='Manual',[string[]]$Tags=@())
    $hex=if($Alpha -eq 255){'#{0:X2}{1:X2}{2:X2}' -f $Red,$Green,$Blue}else{'#{0:X2}{1:X2}{2:X2}{3:X2}' -f $Red,$Green,$Blue,$Alpha}
    $hsv=Convert-WinCareRgbToHsv -Red $Red -Green $Green -Blue $Blue
    [pscustomobject]@{Id=[guid]::NewGuid().ToString('N');Name=$(if($Name){$Name}else{Get-WinCareNearestColorName -Red $Red -Green $Green -Blue $Blue});Hex=$hex;Red=$Red;Green=$Green;Blue=$Blue;Alpha=$Alpha;Rgb="rgb($Red, $Green, $Blue)";Hsl=$hsv.Hsl;Hsv=$hsv.Hsv;CreatedAt=[datetime]::UtcNow.ToString('o');Source=$Source;Tags=@($Tags)}
}

function Convert-WinCareRgbToHsv {
    param([int]$Red,[int]$Green,[int]$Blue)
    $r=$Red/255.0;$g=$Green/255.0;$b=$Blue/255.0;$max=[math]::Max($r,[math]::Max($g,$b));$min=[math]::Min($r,[math]::Min($g,$b));$delta=$max-$min
    $h=0.0;if($delta -ne 0){if($max -eq $r){$h=60*((($g-$b)/$delta)%6)}elseif($max -eq $g){$h=60*((($b-$r)/$delta)+2)}else{$h=60*((($r-$g)/$delta)+4)}};if($h -lt 0){$h+=360}
    $s=if($max -eq 0){0}else{$delta/$max};$v=$max;$l=($max+$min)/2;$hslS=if($delta -eq 0){0}else{$delta/(1-[math]::Abs((2*$l)-1))}
    [pscustomobject]@{Hue=[math]::Round($h,1);Saturation=[math]::Round($s*100,1);Value=[math]::Round($v*100,1);Lightness=[math]::Round($l*100,1);Hsv=('hsv({0}, {1}%, {2}%)' -f [math]::Round($h),[math]::Round($s*100),[math]::Round($v*100));Hsl=('hsl({0}, {1}%, {2}%)' -f [math]::Round($h),[math]::Round($hslS*100),[math]::Round($l*100))}
}

function Get-WinCareNearestColorName {
    param([int]$Red,[int]$Green,[int]$Blue)
    $known=@{
        Black=@(0,0,0);White=@(255,255,255);Red=@(255,0,0);Lime=@(0,255,0);Blue=@(0,0,255);Yellow=@(255,255,0);Cyan=@(0,255,255);Magenta=@(255,0,255);Silver=@(192,192,192);Gray=@(128,128,128);Maroon=@(128,0,0);Olive=@(128,128,0);Green=@(0,128,0);Purple=@(128,0,128);Teal=@(0,128,128);Navy=@(0,0,128);Orange=@(255,165,0);Pink=@(255,192,203);Brown=@(165,42,42);Gold=@(255,215,0);Violet=@(238,130,238);Indigo=@(75,0,130);Coral=@(255,127,80);Salmon=@(250,128,114);Khaki=@(240,230,140);Turquoise=@(64,224,208);Beige=@(245,245,220);Lavender=@(230,230,250);Crimson=@(220,20,60);Azure=@(0,127,255);Chartreuse=@(127,255,0)
    }
    $best='';$distance=[double]::PositiveInfinity
    foreach($pair in $known.GetEnumerator()){$dr=$Red-[int]$pair.Value[0];$dg=$Green-[int]$pair.Value[1];$db=$Blue-[int]$pair.Value[2];$d=($dr*$dr)+($dg*$dg)+($db*$db);if($d -lt $distance){$distance=$d;$best=[string]$pair.Key}}
    $best
}

function Get-WinCareScreenColor {
    [CmdletBinding()]param([Nullable[int]]$X,[Nullable[int]]$Y)
    if(-not $IsWindows -or -not(Initialize-WinCareProductivityInterop)){throw 'Screen color capture requires Windows productivity interop.'}
    $pixel=if($null -ne $X -and $null -ne $Y){[WinCare.Native.Productivity]::CaptureColor([int]$X,[int]$Y)}else{[WinCare.Native.Productivity]::CaptureCursorColor()}
    $record=ConvertTo-WinCareColorRecord -Red ([int]$pixel.Red) -Green ([int]$pixel.Green) -Blue ([int]$pixel.Blue) -Source "Screen:$($pixel.X),$($pixel.Y)"
    $record|Add-Member -NotePropertyName X -NotePropertyValue ([int]$pixel.X);$record|Add-Member -NotePropertyName Y -NotePropertyValue ([int]$pixel.Y);$record
}

function New-WinCareColorPaletteAddPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Color,[string]$Name,[string[]]$Tags=@())
    $before=Get-WinCareColorPaletteStore -Strict;$beforeHash=Get-WinCareColorPaletteStoreHash $before
    $entry=[ordered]@{Id=if($Color.Id){[string]$Color.Id}else{[guid]::NewGuid().ToString('N')};Name=if($Name){$Name}else{[string]$Color.Name};Hex=[string]$Color.Hex;Red=[int]$Color.Red;Green=[int]$Color.Green;Blue=[int]$Color.Blue;Alpha=[int]$Color.Alpha;CreatedAt=if($Color.CreatedAt){[string]$Color.CreatedAt}else{[datetime]::UtcNow.ToString('o')};Source=[string]$Color.Source;Tags=@($Tags)}
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$before.Revision+1;UpdatedAt=[datetime]::UtcNow.ToString('o');Entries=@($before.Entries)+@($entry)};$null=Test-WinCareColorPaletteStore -Store $after;$afterHash=Get-WinCareColorPaletteStoreHash $after
    $action=New-WinCareAction -Type 'ReplaceColorPaletteStore' -Label "Add $($entry.Hex) to local palette" -Risk Low -Parameters @{Store=$after;ExpectedCurrentHash=$beforeHash} -RequiresAdmin $false -Reversible $true -Compensator @{Type='ReplaceColorPaletteStore';Parameters=@{Store=$before;ExpectedCurrentHash=$afterHash}} -TimeoutSeconds 30 -Tags @('Color','Palette','Idempotent') -SourceRecords @('SRC:c4d1981a20') -RecoveryDescription 'Restore the exact prior local color palette.'
    New-WinCarePlan -Title 'Add color to palette' -Actions @($action) -SourceRecords @('SRC:c4d1981a20')
}

function New-WinCareColorPaletteRemovePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Id)
    $before=Get-WinCareColorPaletteStore -Strict;$entry=@($before.Entries|Where-Object Id -eq $Id)|Select-Object -First 1;if(-not $entry){throw 'Palette entry was not found.'};$beforeHash=Get-WinCareColorPaletteStoreHash $before
    $after=[ordered]@{SchemaVersion=1;Revision=[long]$before.Revision+1;UpdatedAt=[datetime]::UtcNow.ToString('o');Entries=@($before.Entries|Where-Object Id -ne $Id)};$afterHash=Get-WinCareColorPaletteStoreHash $after
    $action=New-WinCareAction -Type 'ReplaceColorPaletteStore' -Label "Remove $($entry.Hex) from local palette" -Risk Low -Parameters @{Store=$after;ExpectedCurrentHash=$beforeHash} -RequiresAdmin $false -Reversible $true -Compensator @{Type='ReplaceColorPaletteStore';Parameters=@{Store=$before;ExpectedCurrentHash=$afterHash}} -TimeoutSeconds 30 -Tags @('Color','Palette') -SourceRecords @('SRC:c4d1981a20') -RecoveryDescription 'Restore the exact prior local color palette.'
    New-WinCarePlan -Title 'Remove color from palette' -Actions @($action) -SourceRecords @('SRC:c4d1981a20')
}

function Set-WinCareColorPaletteStoreInternal {
    param([Parameter(Mandatory)][Collections.IDictionary]$Store)
    $null=Test-WinCareColorPaletteStore -Store $Store
    Write-WinCareProtectedJson -LiteralPath (Get-WinCareColorPalettePath) -InputObject $Store -Purpose 'WinCare.ColorPalette'
    $actual=Get-WinCareColorPaletteStore -Strict
    if((Get-WinCareColorPaletteStoreHash $actual) -ne (Get-WinCareColorPaletteStoreHash $Store)){throw 'Color palette verification failed.'}
    $actual
}

function Invoke-WinCareReplaceColorPaletteStoreAction {
    param([object]$Action)
    $store=ConvertTo-WinCareParameterDictionary $Action.Parameters.Store;$null=Test-WinCareColorPaletteStore -Store $store
    $current=Get-WinCareColorPaletteStore -Strict;$currentHash=Get-WinCareColorPaletteStoreHash $current
    if($currentHash -ne [string]$Action.Parameters.ExpectedCurrentHash){return New-WinCareResult -Success $false -Status Blocked -Code 'ColorPaletteChanged' -Message 'Color palette changed after preview.' -ExitCode 74}
    try{$actual=Set-WinCareColorPaletteStoreInternal -Store $store;New-WinCareResult -Success $true -Message 'Color palette updated and verified.' -Data @{Revision=$actual.Revision;Entries=@($actual.Entries).Count}}
    catch{
        $failure=$_.Exception.Message;$warnings=@()
        try{$null=Set-WinCareColorPaletteStoreInternal -Store $current;$warnings+='The previous color palette was restored after the failed update.'}
        catch{$warnings+="The failed palette update could not be fully restored: $($_.Exception.Message)"}
        New-WinCareResult -Success $false -Message "Color palette update failed: $failure" -Warnings $warnings -ExitCode 1
    }
}

function Get-WinCareImageMetadata {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $file=Get-Item -LiteralPath $path -ErrorAction Stop
    if($file.Length -gt 256MB){throw 'Image metadata input exceeds the 256 MiB file-size limit.'}
    $extension=[IO.Path]::GetExtension($path).ToLowerInvariant();if($extension -notin @('.bmp','.gif','.jpg','.jpeg','.png','.tif','.tiff','.ico')){throw 'Image type is not on the metadata allowlist.'}
    try{
        try{Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop}catch{Add-Type -AssemblyName System.Drawing -ErrorAction Stop}
        $image=[Drawing.Image]::FromFile($path,$false)
        try{
            if([long]$image.Width -gt 100000 -or [long]$image.Height -gt 100000 -or ([long]$image.Width * [long]$image.Height) -gt 500000000){throw 'Image dimensions exceed the metadata safety limit.'}
            $frames=1;$delays=@();if($image.FrameDimensionsList.Count -gt 0){$dimension=[Drawing.Imaging.FrameDimension]::new($image.FrameDimensionsList[0]);$frames=$image.GetFrameCount($dimension)}
            try{if($image.PropertyIdList -contains 0x5100){$bytes=$image.GetPropertyItem(0x5100).Value;for($i=0;$i+3-lt$bytes.Length;$i+=4){$delay=[BitConverter]::ToInt32($bytes,$i)*10;$delays+= [math]::Max(10,$delay)}}}catch{Write-Verbose 'Animation frame delays were unavailable.'}
            [pscustomobject]@{Path=$path;Sha256=Get-WinCareSha256 $path;Bytes=[long]$file.Length;Width=[int]$image.Width;Height=[int]$image.Height;PixelFormat=$image.PixelFormat.ToString();RawFormat=$image.RawFormat.Guid.ToString();FrameCount=[int]$frames;FrameDelaysMilliseconds=@($delays|Select-Object -First 10000);Animated=$frames -gt 1;MetadataOnly=$true}
        }finally{$image.Dispose()}
    }catch{throw "Image metadata could not be read: $($_.Exception.Message)"}
}


