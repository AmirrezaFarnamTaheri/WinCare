function Format-WinCareColorRow {param($Item,$Width);"$(Limit-WinCareText $Item.Name 20) $(Limit-WinCareText $Item.Hex 10) $(Limit-WinCareText $Item.Source ([math]::Max(20,$Width-46)))"}
function Show-WinCareColorStudioScreen {
    while($true){
        $palette=@(Get-WinCareColorPalette)
        $menu=@(
            [pscustomobject]@{Label='Capture cursor color';Description='Read one screen pixel through a bounded Win32 capture';Action='Capture'},
            [pscustomobject]@{Label='Palette';Description="$($palette.Count) local color record(s)";Action='Palette'},
            [pscustomobject]@{Label='Remove palette color';Description='Reversible local palette mutation';Action='Remove';Disabled=$palette.Count -eq 0},
            [pscustomobject]@{Label='Image metadata';Description='Dimensions, format, bounded frame count and GIF delays; no rendering engine';Action='Image'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Color and visual studio' -Subtitle 'Pixel inspection, normalized color formats, named colors, palettes, and bounded image metadata' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'Capture'{$color=Get-WinCareScreenColor;Show-WinCareTextPage -Title 'Captured color' -Lines @("Position: $($color.X),$($color.Y)","Name: $($color.Name)","HEX: $($color.Hex)","RGB: $($color.Rgb)","HSL: $($color.Hsl)","HSV: $($color.Hsv)");$save=Read-WinCareInput -Prompt 'Add to palette? (y/N)' -Default 'N';if($save -match '^(?i)y'){$name=Read-WinCareInput -Prompt 'Optional name' -Default $color.Name;Invoke-WinCarePlanFromUi (New-WinCareColorPaletteAddPlan -Color $color -Name $name)}}
                'Palette'{$selected=Show-WinCareListSelector -Title 'Local color palette' -Items $palette -Formatter ${function:Format-WinCareColorRow} -SearchProperties @('Name','Hex','Source','Tags');if($selected){$hsv=Convert-WinCareRgbToHsv -Red $selected.Red -Green $selected.Green -Blue $selected.Blue;Show-WinCareTextPage -Title $selected.Name -Lines @("HEX: $($selected.Hex)","RGB: rgb($($selected.Red), $($selected.Green), $($selected.Blue))","HSL: $($hsv.Hsl)","HSV: $($hsv.Hsv)","Alpha: $($selected.Alpha)","Source: $($selected.Source)","Created: $($selected.CreatedAt)","Tags: $(@($selected.Tags)-join ', ')")}}
                'Remove'{$selected=Show-WinCareListSelector -Title 'Remove palette color' -Items $palette -Formatter ${function:Format-WinCareColorRow} -SearchProperties @('Name','Hex');if($selected){Invoke-WinCarePlanFromUi (New-WinCareColorPaletteRemovePlan -Id $selected.Id)}}
                'Image'{$path=Read-WinCareInput -Prompt 'Image path';if($path){$m=Get-WinCareImageMetadata -LiteralPath $path;Show-WinCareTextPage -Title 'Image metadata' -Lines @("Path: $($m.Path)","SHA-256: $($m.Sha256)","Bytes: $($m.Bytes)","Dimensions: $($m.Width)x$($m.Height)","Pixel format: $($m.PixelFormat)","Frame count: $($m.FrameCount)","Animated: $($m.Animated)","Frame delays ms: $(@($m.FrameDelaysMilliseconds)-join ', ')")}}
            }
        }catch{Show-WinCareMessage -Title 'Color operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

