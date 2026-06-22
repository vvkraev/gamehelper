<#
.SYNOPSIS
    Скрейпит аффиксы для заданного типа предмета с poe2db.tw и выводит в формате affix_library.json.

.PARAMETER ItemTypes
    Список URL-сегментов poe2db.tw через запятую.
    Например: "Wands,Staves,Helmets_str"
    По умолчанию: все известные типы.

.PARAMETER OutputFile
    Путь к выходному JSON-файлу (affix_library.json). По умолчанию — stdout.

.PARAMETER RawDataFile
    Путь для сохранения сырых данных скрейпинга (extracted ModsView JSON per page).
    Позволяет инспектировать данные и повторно запускать обработку без обращения к сайту.
    По умолчанию не сохраняется.

.PARAMETER CacheDir
    Папка для кеша скачанных HTML. По умолчанию: $env:TEMP\poe2db_cache

.PARAMETER ForceRefresh
    Игнорировать кеш, скачать заново.

.NOTES
    Тиры назначаются глобально по семейству ModFamilyList на странице:
    все уникальные ilvl сортируются по убыванию → T1 = наибольший ilvl.
    Это воспроизводит логику poe2db.tw ModsView JavaScript.

.EXAMPLE
    .\poe2db_scrape.ps1 -ItemTypes Wands
    .\poe2db_scrape.ps1 -ItemTypes "Wands,Staves" -OutputFile out.json
    .\poe2db_scrape.ps1 -OutputFile affix_library.json -RawDataFile scrape_raw_data.json
    .\poe2db_scrape.ps1 -OutputFile affix_library.json  # uses cached HTML, no web requests
#>
param(
    [string]$ItemTypes   = "",
    [string]$OutputFile  = "",
    [string]$RawDataFile = "",
    [string]$CacheDir    = "$env:TEMP\poe2db_cache",
    [switch]$ForceRefresh
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Web

# ── Master URL → ItemClass mapping ────────────────────────────────────────────
$AllTypeDefs = @(
    # Weapons
    @{ Url="Wands";                   Class="Wands" }
    @{ Url="Staves";                  Class="Staves" }
    @{ Url="Bows";                    Class="Bows" }
    @{ Url="Crossbows";               Class="Crossbows" }
    @{ Url="One_Hand_Axes";           Class="One Hand Axes" }
    @{ Url="Two_Hand_Axes";           Class="Two Hand Axes" }
    @{ Url="One_Hand_Swords";         Class="One Hand Swords" }
    @{ Url="Two_Hand_Swords";         Class="Two Hand Swords" }
    @{ Url="One_Hand_Maces";          Class="One Hand Maces" }
    @{ Url="Two_Hand_Maces";          Class="Two Hand Maces" }
    @{ Url="Daggers";                 Class="Daggers" }
    @{ Url="Claws";                   Class="Claws" }
    @{ Url="Sceptres";                Class="Sceptres" }
    @{ Url="Quarterstaves";           Class="Quarterstaffs" }
    @{ Url="Spears";                  Class="Spears" }
    @{ Url="Flails";                  Class="Flails" }
    @{ Url="Foci";                    Class="Foci" }
    # Accessories
    @{ Url="Rings";                   Class="Rings" }
    @{ Url="Amulets";                 Class="Amulets" }
    @{ Url="Belts";                   Class="Belts" }
    # Armour – all sub-variants merged into one class
    @{ Url="Gloves_str";              Class="Gloves" }
    @{ Url="Gloves_dex";              Class="Gloves" }
    @{ Url="Gloves_int";              Class="Gloves" }
    @{ Url="Gloves_str_dex";          Class="Gloves" }
    @{ Url="Gloves_dex_int";          Class="Gloves" }
    @{ Url="Gloves_str_int";          Class="Gloves" }
    @{ Url="Boots_str";               Class="Boots" }
    @{ Url="Boots_dex";               Class="Boots" }
    @{ Url="Boots_int";               Class="Boots" }
    @{ Url="Boots_str_dex";           Class="Boots" }
    @{ Url="Boots_dex_int";           Class="Boots" }
    @{ Url="Boots_str_int";           Class="Boots" }
    @{ Url="Helmets_str";             Class="Helmets" }
    @{ Url="Helmets_dex";             Class="Helmets" }
    @{ Url="Helmets_int";             Class="Helmets" }
    @{ Url="Helmets_str_dex";         Class="Helmets" }
    @{ Url="Helmets_dex_int";         Class="Helmets" }
    @{ Url="Helmets_str_int";         Class="Helmets" }
    @{ Url="Body_Armours_str";        Class="Body Armours" }
    @{ Url="Body_Armours_dex";        Class="Body Armours" }
    @{ Url="Body_Armours_int";        Class="Body Armours" }
    @{ Url="Body_Armours_str_dex";    Class="Body Armours" }
    @{ Url="Body_Armours_dex_int";    Class="Body Armours" }
    @{ Url="Body_Armours_str_int";    Class="Body Armours" }
    @{ Url="Body_Armours_str_dex_int";Class="Body Armours" }
    @{ Url="Shields_str";             Class="Shields" }
    @{ Url="Shields_str_dex";         Class="Shields" }
    @{ Url="Shields_str_int";         Class="Shields" }
    @{ Url="Bucklers";                Class="Bucklers" }
    @{ Url="Quivers";                 Class="Quivers" }
    # Flasks
    @{ Url="Life_Flasks";             Class="Life Flasks" }
    @{ Url="Mana_Flasks";             Class="Mana Flasks" }
    # Jewels
    @{ Url="Diamond";                 Class="Diamond Jewels" }
    @{ Url="Emerald";                 Class="Emerald Jewels" }
    @{ Url="Ruby";                    Class="Ruby Jewels" }
    @{ Url="Sapphire";                Class="Sapphire Jewels" }
    @{ Url="Time-Lost_Diamond";       Class="Time-Lost Diamond Jewels" }
    @{ Url="Time-Lost_Emerald";       Class="Time-Lost Emerald Jewels" }
    @{ Url="Time-Lost_Ruby";          Class="Time-Lost Ruby Jewels" }
    @{ Url="Time-Lost_Sapphire";      Class="Time-Lost Sapphire Jewels" }
    # Tablets — game reports all tablet types as "Item Class: Tablet"; SubClass from URL prefix
    @{ Url="Abyss_Tablet";            Class="Tablet"; SubClass="Abyss" }
    @{ Url="Breach_Tablet";           Class="Tablet"; SubClass="Breach" }
    @{ Url="Delirium_Tablet";         Class="Tablet"; SubClass="Delirium" }
    @{ Url="Expedition_Tablet";       Class="Tablet"; SubClass="Expedition" }
    @{ Url="Irradiated_Tablet";       Class="Tablet"; SubClass="Irradiated" }
    @{ Url="Overseer_Tablet";         Class="Tablet"; SubClass="Overseer" }
    @{ Url="Ritual_Tablet";           Class="Tablet"; SubClass="Ritual" }
    @{ Url="Temple_Tablet";           Class="Tablet"; SubClass="Temple" }
    # Other
    @{ Url="Charms";                  Class="Charms" }
    @{ Url="Talismans";               Class="Talismans" }
    @{ Url="Traps";                   Class="Traps" }
)

# ── Determine which types to process ──────────────────────────────────────────
$requestedUrls = if ($ItemTypes) {
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $ItemTypes -split ',' | ForEach-Object { $set.Add($_.Trim()) | Out-Null }
    $set
} else { $null }

$typesToProcess = if ($requestedUrls) {
    $AllTypeDefs | Where-Object { $requestedUrls.Contains($_.Url) }
} else { $AllTypeDefs }

# ── Cache setup ────────────────────────────────────────────────────────────────
if (-not (Test-Path $CacheDir)) { New-Item -ItemType Directory -Path $CacheDir | Out-Null }

# ── Helper: fetch HTML (with file cache) ──────────────────────────────────────
function Get-PageHtml([string]$urlPath) {
    $safeName  = $urlPath -replace '[^a-zA-Z0-9_-]', '_'
    $cacheFile = Join-Path $CacheDir "$safeName.html"

    if (-not $ForceRefresh -and (Test-Path $cacheFile)) {
        Write-Host "  [cache] $urlPath" -ForegroundColor DarkGray
        return Get-Content $cacheFile -Raw
    }

    Write-Host "  [fetch] https://poe2db.tw/us/$urlPath ..." -ForegroundColor Cyan
    $tmpFile = [System.IO.Path]::GetTempFileName()
    try {
        & curl.exe -s --max-time 60 -A "Mozilla/5.0" "https://poe2db.tw/us/$urlPath" -o $tmpFile
        if ($LASTEXITCODE -ne 0) { throw "curl.exe exited with $LASTEXITCODE" }
        $html = Get-Content $tmpFile -Raw
        if (-not $html -or $html.Length -lt 1000) { throw "Response too short ($($html.Length) bytes)" }
        $html | Out-File $cacheFile -Encoding utf8
        return $html
    } finally {
        Remove-Item $tmpFile -ErrorAction SilentlyContinue
    }
}

# ── Helper: extract ModsView JSON ─────────────────────────────────────────────
function Get-ModsViewData([string]$html) {
    $start = $html.IndexOf('new ModsView(')
    if ($start -lt 0) { throw "ModsView initializer not found in page" }

    $i = $html.IndexOf('(', $start)
    $depth = 0; $jsonEnd = $i
    for ($j = $i; $j -lt $html.Length; $j++) {
        $c = $html[$j]
        if ($c -eq '(') { $depth++ }
        elseif ($c -eq ')') { $depth--; if ($depth -eq 0) { $jsonEnd = $j; break } }
    }

    return $html.Substring($i + 1, $jsonEnd - $i - 1) | ConvertFrom-Json
}

# ── Helper: HTML → clean text + range ─────────────────────────────────────────
function Parse-ModStr([string]$rawHtml) {
    $text = $rawHtml -replace '<[^>]+>', ''
    $text = [System.Web.HttpUtility]::HtmlDecode($text)
    # Fix UTF-8 en-dash (U+2013) mojibake when page was read as Latin-1
    $text = $text -replace ([char]0x0432 + [char]0x0402 + [char]0x201D), [char]0x2013
    $text = ($text -replace '\s+', ' ').Trim()

    $range = $null
    if ($text -match '\((\d[\d,\.]*)[^\d)]+(\d[\d,\.]*)\)') {
        $range = "$($Matches[1] -replace ',','')-$($Matches[2] -replace ',','')"
    }

    return @{ Text = $text; Range = $range }
}

# ── Helper: crafting tags → armour AffixSubClass ──────────────────────────────
# Извлекает data-tag значения из mod_no HTML и маппит комбинацию defence-тегов
# в стандартные строки подтипа ("Armour", "Evasion", "Energy Shield" и гибриды).
# null = нет defence-тегов → аффикс доступен на всех подтипах.
$DefenceTags = @('armour','evasion','energy_shield')

function Get-ArmourSubClass([object]$modNo) {
    $tags = @()
    if ($null -ne $modNo) {
        $items = if ($modNo -is [array]) { $modNo } else { @([string]$modNo) }
        foreach ($item in $items) {
            if ([string]$item -match 'data-tag="([^"]+)"') { $tags += $Matches[1] }
        }
    }
    $hasArmour = $tags -contains 'armour'
    $hasEvasion = $tags -contains 'evasion'
    $hasES     = $tags -contains 'energy_shield'
    if (!$hasArmour -and !$hasEvasion -and !$hasES) { return $null }
    if ($hasArmour -and $hasEvasion -and $hasES)    { return $null }  # тройной гибрид — нет ограничения
    if ($hasArmour -and $hasEvasion)  { return 'Armour/Evasion' }
    if ($hasArmour -and $hasES)       { return 'Armour/Energy Shield' }
    if ($hasEvasion -and $hasES)      { return 'Evasion/Energy Shield' }
    if ($hasArmour)                   { return 'Armour' }
    if ($hasEvasion)                  { return 'Evasion' }
    return 'Energy Shield'
}

# ── Helper: process one page's mods into library entries ──────────────────────
# Tier assignment: mirrors poe2db.tw ModsView JavaScript.
# Within each ModFamilyList group, collect all unique ilvl values across the ENTIRE family,
# sort descending, and assign tier = rank(ilvl). This means mods of different sub-families
# (e.g. chaos vs. cold spell skills) that share a parent family (IncreaseSocketedGemLevel)
# get globally-ranked tiers — T1=best ilvl in family, gaps appear where no sub-family
# has a mod at that ilvl level.
function Convert-ModsToEntries([array]$mods, [string]$itemClass, [string]$typeSubClass = "") {
    $entries = [System.Collections.Generic.List[hashtable]]::new()

    # Step 1: group by ModFamilyList[0]
    # ModFamilyList can be an array OR a plain string depending on the page/mod type.
    $byFamily = @{}
    foreach ($mod in $mods) {
        $fl = $mod.ModFamilyList
        $family = if ($null -eq $fl) {
            [string]$mod.Name
        } elseif ($fl -is [array]) {
            if ($fl.Length -gt 0) { [string]$fl[0] } else { [string]$mod.Name }
        } else {
            # Single string value
            $s = [string]$fl
            if ($s.Length -gt 0) { $s } else { [string]$mod.Name }
        }
        if (-not $byFamily.ContainsKey($family)) {
            $byFamily[$family] = [System.Collections.Generic.List[object]]::new()
        }
        $byFamily[$family].Add($mod)
    }

    foreach ($family in $byFamily.Keys) {
        $familyMods = @($byFamily[$family])

        # Step 2: build global ilvl→tier map for the entire family
        # (unique ilvls sorted descending, T1 = highest ilvl)
        # @() forces array so .Count works in PS 5.1 even when only one unique ilvl exists
        $uniqueLevels = @(
            $familyMods |
            ForEach-Object { [int]$_.Level } |
            Sort-Object -Unique -Descending
        )

        $tierMap = @{}
        for ($t = 0; $t -lt $uniqueLevels.Count; $t++) {
            $tierMap[[int]$uniqueLevels[$t]] = $t + 1
        }

        # Step 3: emit one entry per mod, using global tier
        foreach ($mod in $familyMods) {
            $parsed      = Parse-ModStr $mod.str
            $genId       = [int]$mod.ModGenerationTypeID
            $ilvl        = [int]$mod.Level
            # Weight from DropChance (tier weight for orb selection)
            $weight      = if ($null -ne $mod.DropChance) { [int]$mod.DropChance } else { $null }
            # SubClass: for Tablets use $typeSubClass (e.g. "Breach"), for armour derive from mod_no data-tags
            $subClass    = if ($typeSubClass -ne "") {
                $typeSubClass
            } else {
                Get-ArmourSubClass $mod.mod_no
            }
            $entry = @{
                itemClasses    = @($itemClass)
                affixType      = if ($genId -eq 1) { "Prefix Modifier" } else { "Suffix Modifier" }
                affixName      = [string]$mod.Name
                affixTier      = $tierMap[$ilvl]
                affixTierLevel = $ilvl
                affixStats     = @($parsed.Text)
                affixRanges    = @($parsed.Range)
            }
            if ($null -ne $weight)   { $entry.weight        = $weight }
            if ($null -ne $subClass) { $entry.affixSubClass = $subClass }
            $entries.Add($entry)
        }
    }

    return $entries
}

# ── Main ───────────────────────────────────────────────────────────────────────
$seenKeys   = [System.Collections.Generic.HashSet[string]]::new()
$allEntries = [System.Collections.Generic.List[hashtable]]::new()
# Raw data: url → array of stripped mod objects (Name, Level, ModGenerationTypeID, ModFamilyList, str, hover, DropChance?, mod_no?)
$rawPages   = [ordered]@{}

foreach ($def in $typesToProcess) {
    $urlPath   = $def.Url
    $itemClass = $def.Class
    Write-Host "Processing: $urlPath -> '$itemClass'" -ForegroundColor Green

    try {
        $html    = Get-PageHtml $urlPath
        $data    = Get-ModsViewData $html
        $mods    = $data.normal
        if (-not $mods -or $mods.Count -eq 0) {
            Write-Warning "  No normal mods for $urlPath"
            continue
        }
        Write-Host "  Found $($mods.Count) mods"

        # Save raw data (compact: only fields needed to reproduce tier/stat computation)
        if ($RawDataFile) {
            $rawPages[$urlPath] = @($mods | ForEach-Object {
                $fl = $_.ModFamilyList
                $flArr = if ($null -eq $fl) { @() }
                         elseif ($fl -is [array]) { @($fl | ForEach-Object { [string]$_ }) }
                         else { @([string]$fl) }
                $raw = @{
                    Name               = [string]$_.Name
                    Level              = [int]$_.Level
                    ModGenerationTypeID= [int]$_.ModGenerationTypeID
                    ModFamilyList      = $flArr
                    str                = [string]$_.str
                    hover              = [string]$_.hover
                }
                if ($null -ne $_.DropChance) { $raw.DropChance = [int]$_.DropChance }
                if ($null -ne $_.mod_no)     { $raw.mod_no     = $_.mod_no }
                $raw
            })
        }

        $subClass = if ($def.ContainsKey('SubClass')) { $def.SubClass } else { "" }
        $entries = Convert-ModsToEntries $mods $itemClass $subClass
        $added   = 0
        foreach ($e in $entries) {
            # Dedup key: class + subClass + name + stats[0] + ranges[0]
            $sc  = if ($e.ContainsKey('affixSubClass')) { $e.affixSubClass } else { "" }
            $key = "$($e.itemClasses[0])|$sc|$($e.affixName)|$($e.affixStats[0])|$($e.affixRanges[0])"
            if ($seenKeys.Add($key)) {
                $allEntries.Add($e)
                $added++
            }
        }
        Write-Host "  Added $added new entries ($($entries.Count - $added) duplicates skipped)"
    } catch {
        Write-Warning "  FAILED: $_"
    }
}

Write-Host "`nTotal entries: $($allEntries.Count)" -ForegroundColor Yellow

# ── Save raw data ──────────────────────────────────────────────────────────────
if ($RawDataFile) {
    $rawOutput = @{
        scrapedAt = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        pageCount = $rawPages.Count
        pages     = $rawPages
    }
    ($rawOutput | ConvertTo-Json -Depth 10) | Out-File $RawDataFile -Encoding utf8
    Write-Host "Raw data saved to: $RawDataFile" -ForegroundColor Green
}

# ── Serialize library ──────────────────────────────────────────────────────────
$output = @{ version = 1; entries = $allEntries.ToArray() }
$json   = $output | ConvertTo-Json -Depth 10

if ($OutputFile) {
    $json | Out-File $OutputFile -Encoding utf8
    Write-Host "Saved to: $OutputFile" -ForegroundColor Green
} else {
    $json
}
