<#
.SYNOPSIS
  Downloads official parallel-art ("alt art") card images for OPTCGSim and generates the
  matching _small thumbnails, so the OPSimExtensions Alt Art selector can offer them.

.DESCRIPTION
  For every base card image in <GameDir>\OPTCGSim_Data\StreamingAssets\Cards\<Set>\, this probes
  the official English card database image host for parallel versions:
      https://en.onepiece-cardgame.com/images/cardlist/card/<CardID>_p<N>.png
  With -IncludeJapanese, any variant slot missing on the EN site is also tried on the official
  Japanese site (same art, Japanese card text):
      https://www.onepiece-cardgame.com/images/cardlist/card/<CardID>_p<N>.png
  The JP fallback is skipped for the P (promo) set, because EN and JP promo numbering differ —
  the same P-xxx ID can be a different card entirely.

  Each found variant is saved next to the base art as <CardID>_p<N>.png plus a
  <CardID>_p<N>_small.jpg thumbnail (120x167, same as the sim's own thumbnails).
  Probing continues past a missing _pN and only stops after 2 consecutive missing slots,
  so gapped numbering (e.g. a card with _p2 but no _p1 on a given site) is still found.

  Already-downloaded variants are skipped, so re-running is cheap and resumable.

.EXAMPLE
  .\Fetch-AltArts.ps1 -Sets OP01,OP02
.EXAMPLE
  .\Fetch-AltArts.ps1 -All -IncludeJapanese
#>
param(
    [string[]]$Sets = @("OP01"),
    [switch]$All,
    [switch]$IncludeJapanese,
    [switch]$TagJapanese,
    [string]$GameDir = "D:\OPSIM",
    [int]$MaxVariantsPerCard = 9,
    [int]$DelayMs = 150
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
[Net.ServicePointManager]::SecurityProtocol = `
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$cardsRoot = Join-Path $GameDir "OPTCGSim_Data\StreamingAssets\Cards"
if (-not (Test-Path $cardsRoot)) { throw "Cards folder not found: $cardsRoot" }

# Variants whose image came from the JP site (Japanese rules text). LogPose reads this to
# show the base English card in the enlarged hover preview for these.
$jpManifest = Join-Path $cardsRoot "jp-variants.txt"
$jpTags = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
if (Test-Path $jpManifest) {
    foreach ($line in Get-Content $jpManifest) {
        $t = $line.Trim()
        if ($t.Length -gt 0 -and -not $t.StartsWith('#')) { [void]$jpTags.Add($t) }
    }
}
$jpTagsDirty = $false

function Save-JpManifest {
    if (-not $jpTagsDirty) { return }
    $sorted = @($jpTags) | Sort-Object
    @('# Variants downloaded from the Japanese card site (Japanese rules text).') + $sorted |
        Set-Content $jpManifest -Encoding utf8
    Write-Host "jp-variants.txt updated: $($jpTags.Count) entries." -ForegroundColor Yellow
}

if ($All) {
    $Sets = Get-ChildItem $cardsRoot -Directory | Where-Object { $_.Name -ne "Don" } | Select-Object -ExpandProperty Name
}

$enBase = "https://en.onepiece-cardgame.com/images/cardlist/card"
$jpBase = "https://www.onepiece-cardgame.com/images/cardlist/card"
$client = New-Object System.Net.WebClient
$client.Headers.Add("User-Agent", "Mozilla/5.0 (OPTCGSim alt-art fetcher; personal use)")

function New-Thumbnail([string]$SourcePng, [string]$DestJpg) {
    $src = [System.Drawing.Image]::FromFile($SourcePng)
    try {
        $thumb = New-Object System.Drawing.Bitmap 120, 167
        $g = [System.Drawing.Graphics]::FromImage($thumb)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($src, 0, 0, 120, 167)
        $g.Dispose()
        $jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
        $encParams = New-Object System.Drawing.Imaging.EncoderParameters 1
        $encParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]85)
        $thumb.Save($DestJpg, $jpegCodec, $encParams)
        $thumb.Dispose()
    } finally {
        $src.Dispose()
    }
}

function Try-Download([string]$Url, [string]$Dest) {
    try {
        $data = $client.DownloadData($Url)
    } catch {
        return $false
    }
    [System.IO.File]::WriteAllBytes($Dest, $data)
    return $true
}

function Test-UrlExists([string]$Url) {
    try {
        $req = [System.Net.WebRequest]::Create($Url)
        $req.Method = "HEAD"
        $req.UserAgent = "Mozilla/5.0 (OPTCGSim alt-art fetcher; personal use)"
        $req.Timeout = 10000
        $resp = $req.GetResponse()
        $resp.Close()
        return $true
    } catch {
        return $false
    }
}

# Retro-tagging: for every already-downloaded _pN variant, ask the EN site whether that
# variant exists there. If it does not, our local copy came from the JP fallback.
if ($TagJapanese) {
    $probe = "$enBase/OP01-001_p1.png"
    if (-not (Test-UrlExists $probe)) {
        throw "HEAD probe against a known-good EN url failed ($probe) - cannot tag reliably."
    }
    $tagged = 0; $checked = 0
    foreach ($set in $Sets) {
        if ($set -eq "P") { continue }   # JP fallback never ran for promos
        $setDir = Join-Path $cardsRoot $set
        if (-not (Test-Path $setDir)) { continue }
        $variants = Get-ChildItem $setDir -File -Filter "*_p*.png" | Where-Object {
            $_.BaseName -match "_p\d+$"
        } | Select-Object -ExpandProperty BaseName
        Write-Host "=== $set : $($variants.Count) variants to check ===" -ForegroundColor Cyan
        foreach ($v in $variants) {
            $checked++
            if ($jpTags.Contains($v)) { continue }
            if (-not (Test-UrlExists "$enBase/$v.png")) {
                [void]$jpTags.Add($v)
                $script:jpTagsDirty = $true
                $tagged++
                Write-Host "  $v  [JP]"
            }
            Start-Sleep -Milliseconds $DelayMs
        }
    }
    Save-JpManifest
    Write-Host "Tagging done: checked $checked variants, $tagged newly tagged as JP." -ForegroundColor Green
    return
}

$totalEN = 0
$totalJP = 0
$totalSkipped = 0
foreach ($set in $Sets) {
    $setDir = Join-Path $cardsRoot $set
    if (-not (Test-Path $setDir)) { Write-Warning "Set folder not found, skipping: $setDir"; continue }
    $jpAllowed = $IncludeJapanese -and ($set -ne "P")

    $baseCards = Get-ChildItem $setDir -File | Where-Object {
        $_.Extension -match "^\.(png|jpg)$" -and $_.BaseName -notmatch "_"
    } | Select-Object -ExpandProperty BaseName | Sort-Object -Unique

    Write-Host "=== $set : $($baseCards.Count) cards ===" -ForegroundColor Cyan
    foreach ($cardId in $baseCards) {
        $missStreak = 0
        for ($n = 1; $n -le $MaxVariantsPerCard; $n++) {
            $suffix = "_p$n"
            $destPng = Join-Path $setDir "$cardId$suffix.png"
            $destSmall = Join-Path $setDir "$cardId${suffix}_small.jpg"
            if (Test-Path $destPng) {
                $missStreak = 0
                $totalSkipped++
                if (-not (Test-Path $destSmall)) { New-Thumbnail $destPng $destSmall }
                continue
            }
            $got = $null
            if (Try-Download "$enBase/$cardId$suffix.png" $destPng) { $got = "EN" }
            elseif ($jpAllowed -and (Try-Download "$jpBase/$cardId$suffix.png" $destPng)) { $got = "JP" }

            if ($null -eq $got) {
                $missStreak++
                if ($missStreak -ge 2) { break }
                continue
            }
            $missStreak = 0
            New-Thumbnail $destPng $destSmall
            if ($got -eq "EN") { $totalEN++ }
            else {
                $totalJP++
                if ($jpTags.Add("$cardId$suffix")) { $script:jpTagsDirty = $true }
            }
            Write-Host "  $cardId$suffix  [$got]  ($([math]::Round((Get-Item $destPng).Length/1KB)) KB)"
            Start-Sleep -Milliseconds $DelayMs
        }
    }
}
Save-JpManifest
Write-Host "Done. Downloaded $totalEN new EN + $totalJP new JP variants (skipped $totalSkipped already present)." -ForegroundColor Green
