param(
    [string]$SearchId = "D6pLzDEdu5",
    [string]$League   = "Runes of Aldur",
    [int]   $Take     = 10,
    [string]$Session  = ""   # pass POESESSID directly to skip browser extraction
)

$ErrorActionPreference = "Stop"
$helperDir = Join-Path $env:TEMP "poe_price_helper_$(Get-Random)"
New-Item -ItemType Directory -Force $helperDir | Out-Null

$csproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.4" />
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.0.4" />
  </ItemGroup>
</Project>
'@

$cs = @'
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var outFile = args[0];
var browsers = new (string Name, string LocalState, string Cookies)[]
{
    ("Edge",
     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                  "Microsoft", "Edge", "User Data", "Local State"),
     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                  "Microsoft", "Edge", "User Data", "Default", "Network", "Cookies")),
    ("Chrome",
     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                  "Google", "Chrome", "User Data", "Local State"),
     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                  "Google", "Chrome", "User Data", "Default", "Network", "Cookies")),
};
foreach (var (bName, lsPath, cookPath) in browsers)
{
    if (!File.Exists(lsPath) || !File.Exists(cookPath))
    {
        Console.Error.WriteLine($"[{bName}] not found, skipping");
        continue;
    }
    Console.Error.WriteLine($"[{bName}] reading cookies...");
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(lsPath));
        var encKey = Convert.FromBase64String(
            doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!)[5..];
        var aesKey = ProtectedData.Unprotect(encKey, null, DataProtectionScope.CurrentUser);
        var tmpDb  = Path.GetTempFileName();
        using (var src = new FileStream(cookPath, FileMode.Open, FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete))
        using (var dst = new FileStream(tmpDb, FileMode.Create, FileAccess.Write, FileShare.None))
            src.CopyTo(dst);
        try
        {
            using var conn = new SqliteConnection($"Data Source={tmpDb};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT encrypted_value FROM cookies " +
                "WHERE host_key LIKE '%pathofexile.com%' AND name='POESESSID' LIMIT 1";
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read())
            {
                Console.Error.WriteLine($"[{bName}] POESESSID not found - log in at pathofexile.com first");
                continue;
            }
            var blob = (byte[])rdr.GetValue(0);
            var pfx  = Encoding.ASCII.GetString(blob, 0, 3);
            if (pfx == "v20")
            {
                Console.Error.WriteLine($"[{bName}] App-Bound Encryption (v20) - close ALL browser windows and retry");
                continue;
            }
            if (pfx != "v10" && pfx != "v11")
            {
                Console.Error.WriteLine($"[{bName}] unknown encryption format: {pfx}");
                continue;
            }
            var plain = new byte[blob.Length - 3 - 12 - 16];
            using var gcm = new AesGcm(aesKey, tagSizeInBytes: 16);
            gcm.Decrypt(blob[3..15], blob[15..^16], blob[^16..], plain);
            File.WriteAllText(outFile, Encoding.UTF8.GetString(plain));
            Console.Error.WriteLine($"[{bName}] POESESSID extracted OK");
            return;
        }
        finally { try { File.Delete(tmpDb); } catch { } }
    }
    catch (Exception ex) { Console.Error.WriteLine($"[{bName}] error: {ex.Message}"); }
}
Console.Error.WriteLine("Could not extract POESESSID from Edge or Chrome.");
Environment.Exit(1);
'@

[System.IO.File]::WriteAllText((Join-Path $helperDir "App.csproj"), $csproj, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText((Join-Path $helperDir "Program.cs"),  $cs,     [System.Text.Encoding]::UTF8)

try {
    # --- Get session ---
    if (-not [string]::IsNullOrWhiteSpace($Session)) {
        $session = $Session.Trim()
        Write-Host "Using provided POESESSID." -ForegroundColor Green
    } else {
        Write-Host "Building helper (first run ~30s for NuGet restore)..." -ForegroundColor DarkCyan
        $sessionFile = Join-Path $helperDir "session.txt"
        & dotnet run --project (Join-Path $helperDir "App.csproj") -c Release --verbosity quiet -- $sessionFile

        if (-not (Test-Path $sessionFile)) { Write-Error "Session file not created."; exit 1 }
        $session = (Get-Content $sessionFile -Raw).Trim()
        if ([string]::IsNullOrWhiteSpace($session)) { Write-Error "POESESSID is empty."; exit 1 }
        Write-Host "POESESSID extracted from browser." -ForegroundColor Green
    }

    # --- Trade API ---
    $leagueEnc = [Uri]::EscapeDataString($League)
    $hdr = @{
        "Cookie"     = "POESESSID=$session"
        "User-Agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
    }

    # Step 1: GET saved search to extract stored query config
    Write-Host "Loading search config $SearchId ..." -ForegroundColor DarkCyan
    $saved = Invoke-RestMethod `
        -Uri "https://www.pathofexile.com/api/trade2/search/poe2/$leagueEnc/$SearchId" `
        -Headers $hdr

    # Step 2: POST {query, sort} to get live result IDs
    Write-Host "Executing live search ..." -ForegroundColor DarkCyan
    $postBody = @{ query = $saved.query; sort = @{ price = "asc" } } | ConvertTo-Json -Depth 15
    $search = Invoke-RestMethod `
        -Uri "https://www.pathofexile.com/api/trade2/search/poe2/$leagueEnc" `
        -Method Post `
        -Body $postBody `
        -ContentType "application/json" `
        -Headers $hdr
    Write-Host "Total listings: $($search.total)" -ForegroundColor Cyan

    if (-not $search.result -or $search.result.Count -eq 0) { Write-Host "No listings found."; return }

    # Step 3: Fetch item details (max 10 per request per API limit)
    $ids   = ($search.result | Select-Object -First ([Math]::Min($Take, 10))) -join ","
    $fetch = Invoke-RestMethod `
        -Uri "https://www.pathofexile.com/api/trade2/fetch/${ids}?query=$($search.id)&realm=poe2" `
        -Headers $hdr

    # --- Output ---
    Write-Host ""
    Write-Host ("=== Results: top $([Math]::Min($Take,10)) of $($search.total) listings ===") -ForegroundColor Yellow
    Write-Host ("{0,-36} {1,-16} {2}" -f "Account", "Price", "Status") -ForegroundColor Gray
    Write-Host ("-" * 65) -ForegroundColor DarkGray

    foreach ($r in $fetch.result) {
        $p      = $r.listing.price
        $acc    = $r.listing.account
        $status = if ($acc.online) { "ONLINE" } else { "offline" }
        $color  = if ($acc.online) { "Green" } else { "White" }
        $ps     = "$($p.amount) $($p.currency)"
        Write-Host ("{0,-36} {1,-16} {2}" -f $acc.name, $ps, $status) -ForegroundColor $color
        # explicit mods from trade2 API are objects, not plain strings
        foreach ($mod in $r.item.explicitMods) {
            $desc = if ($mod -is [string]) { $mod } else { $mod.description }
            if ($desc) { Write-Host "  $desc" -ForegroundColor DarkGray }
        }
    }

    Write-Host ""
    Write-Host "URL: https://www.pathofexile.com/trade2/search/poe2/$leagueEnc/$SearchId" -ForegroundColor DarkGray

} finally {
    Remove-Item -Recurse -Force $helperDir -ErrorAction SilentlyContinue
}
