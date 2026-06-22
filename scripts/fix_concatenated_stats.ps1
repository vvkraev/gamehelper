# fix_concatenated_stats.ps1
# Splits concatenated affixStats entries in affix_library.json.
# Works line-by-line on the raw JSON — no re-serialization needed.

$LibPath    = Join-Path $PSScriptRoot '..\affix_library.json'
$BackupPath = $LibPath + '.fix_concat.bak'

$splitRx = [regex]'(?<=[)\w])\+(?=\()'
$rangeRx = [regex]'\((\d+)[–\-](\d+)\)'

function Get-RangeStr($text) {
    $m = $rangeRx.Match($text)
    if ($m.Success) { return "$($m.Groups[1].Value)-$($m.Groups[2].Value)" }
    return $null
}

Write-Host "Читаю $LibPath ..."
$lines = [System.IO.File]::ReadAllLines($LibPath, [System.Text.Encoding]::UTF8)

$out           = [System.Collections.Generic.List[string]]::new($lines.Length + 500)
$inStats       = $false
$inRanges      = $false
$pendingRange2 = $null   # second range to inject into affixRanges block
$totalFixed    = 0

foreach ($line in $lines) {
    $trimmed = $line.Trim()

    # Detect section boundaries
    if ($trimmed -eq '"affixStats": [')  { $inStats = $true;  $out.Add($line); continue }
    if ($trimmed -eq '"affixRanges": [') { $inRanges = $true; $out.Add($line); continue }
    if ($trimmed -eq '],' -or $trimmed -eq ']') {
        if ($inStats)  { $inStats  = $false }
        if ($inRanges) { $inRanges = $false }
        $out.Add($line)
        continue
    }

    # Inside affixStats: look for concatenated stat
    if ($inStats -and $trimmed.StartsWith('"') -and $trimmed.EndsWith('"')) {
        # Remove surrounding quotes and trailing comma if any
        $inner = $trimmed.Trim('"')
        $m = $splitRx.Match($inner)
        if ($m.Success -and $m.Index -gt 0) {
            $part1 = $inner.Substring(0, $m.Index)
            $part2 = $inner.Substring($m.Index)
            # Determine indentation from original line
            $indent = $line.Substring(0, $line.Length - $line.TrimStart().Length)
            $out.Add("$indent`"$part1`",")
            $out.Add("$indent`"$part2`"")
            $pendingRange2 = Get-RangeStr $part2
            $totalFixed++
            continue
        }
    }

    # Inside affixRanges: if we have a pending second range, inject after the first range line
    if ($inRanges -and $null -ne $pendingRange2 -and $trimmed.StartsWith('"') -and $trimmed.EndsWith('"')) {
        $indent = $line.Substring(0, $line.Length - $line.TrimStart().Length)
        # First range line: add comma, then insert second range
        $firstRange = $trimmed.Trim('"')
        $out.Add("$indent`"$firstRange`",")
        $out.Add("$indent`"$pendingRange2`"")
        $pendingRange2 = $null
        continue
    }

    $out.Add($line)
}

Write-Host "Разделено склеенных строк: $totalFixed"

if ($totalFixed -eq 0) {
    Write-Host "Ничего не изменилось — возможно, библиотека уже исправлена."
    exit 0
}

Write-Host "Создаю бэкап: $BackupPath"
Copy-Item $LibPath $BackupPath -Force

Write-Host "Записываю исправленную библиотеку..."
[System.IO.File]::WriteAllLines($LibPath, $out.ToArray(), (New-Object System.Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Готово. Разделено $totalFixed склеенных строк статов."
Write-Host "Бэкап: $BackupPath"
