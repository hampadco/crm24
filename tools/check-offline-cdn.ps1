# Scans frontend assets for external CDN / remote URL references.

$ErrorActionPreference = 'Stop'
$crmRoot = Join-Path (Join-Path (Join-Path $PSScriptRoot '..') 'src') 'Crm.Web'
$docsRoot = Join-Path (Join-Path $PSScriptRoot '..') 'docs'
$scanRoots = @($crmRoot, $docsRoot)

$patterns = @(
    'cdn\.datatables\.net',
    'cdn\.plyr\.io',
    'imasdk\.googleapis\.com',
    'unpkg\.com',
    'jsdelivr\.net',
    'cdnjs\.cloudflare\.com',
    'fonts\.googleapis\.com',
    'fonts\.gstatic\.com'
)

$include = @('*.cshtml', '*.html', '*.js', '*.css')
$excludeDirs = @('node_modules', 'bin', 'obj')

$issues = @()
foreach ($root in $scanRoots) {
    Get-ChildItem -Path $root -Recurse -Include $include -File |
        Where-Object {
            $rel = $_.FullName.Substring($root.Length)
            -not ($excludeDirs | ForEach-Object { $rel -like "*\$_\*" })
        } |
        ForEach-Object {
            $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
            if (-not $content) { return }
            foreach ($p in $patterns) {
                if ($content -match $p) {
                    if ($_.FullName -like '*\vendor-cdn\google-ima\*') { continue }
                    $issues += [pscustomobject]@{
                        File = $_.FullName.Substring($root.Length).TrimStart('\')
                        Pattern = $p
                    }
                }
            }
        }
}

$required = @(
    'wwwroot\lib\vendor-cdn\datatables\i18n\English.json',
    'wwwroot\lib\vendor-cdn\datatables\i18n\Persian.json',
    'wwwroot\lib\vendor-cdn\plyr\plyr.svg',
    'wwwroot\lib\vendor-cdn\plyr\blank.mp4',
    'wwwroot\lib\vendor-cdn\google-ima\ima3.js',
    'wwwroot\lib\vazirmatn\fonts\Vazirmatn-Light.woff2',
    'wwwroot\lib\vazirmatn\fonts\Vazirmatn-Regular.woff2',
    'wwwroot\lib\vazirmatn\fonts\Vazirmatn-ExtraBold.woff2'
)

$docFonts = @(
    'assets\fonts\Vazirmatn-Light.woff2',
    'assets\fonts\Vazirmatn-Regular.woff2',
    'assets\fonts\Vazirmatn-ExtraBold.woff2',
    'assets\css\vazirmatn.css'
)

$missing = @()
foreach ($rel in $required) {
    $path = Join-Path $crmRoot $rel
    if (-not (Test-Path -LiteralPath $path)) {
        $missing += $rel
    }
}
foreach ($rel in $docFonts) {
    $path = Join-Path $docsRoot $rel
    if (-not (Test-Path -LiteralPath $path)) {
        $missing += "docs\$rel"
    }
}

Write-Host '=== Offline CDN check ===' -ForegroundColor Cyan

if ($missing.Count -gt 0) {
    Write-Host 'Missing local vendor files:' -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  - $_" }
} else {
    Write-Host 'All required local assets present.' -ForegroundColor Green
}

if ($issues.Count -gt 0) {
    Write-Host 'External CDN references found:' -ForegroundColor Yellow
    $issues | Sort-Object File, Pattern | ForEach-Object {
        Write-Host ("  {0}  ({1})" -f $_.File, $_.Pattern)
    }
    exit 1
}

Write-Host 'No external CDN references found in Crm.Web or docs.' -ForegroundColor Green
exit 0
