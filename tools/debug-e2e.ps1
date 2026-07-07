$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5188'
$email = "user$(Get-Random)@test.ir"

function Get-Token($content) {
    ([regex]::Match($content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
}
function Post-Form($url, $session, [hashtable]$fields) {
    $pairs = foreach ($k in $fields.Keys) {
        "$([System.Uri]::EscapeDataString($k))=$([System.Uri]::EscapeDataString([string]$fields[$k]))"
    }
    Invoke-WebRequest -Uri $url -Method Post -WebSession $session -UseBasicParsing `
        -ContentType 'application/x-www-form-urlencoded' -Body ($pairs -join '&')
}

$reg = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s -UseBasicParsing
$resp = Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'شرکت آزمایشی نور'; FullName = 'علی رضایی'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) }
Write-Output "=== dashboard cards ==="
[regex]::Matches($resp.Content, '<h5 class="mb-1">([^<]+)</h5>') | ForEach-Object { Write-Output $_.Groups[1].Value }

$createPage = Invoke-WebRequest -Uri "$base/App/m/leads/create" -WebSession $s -UseBasicParsing
$created = Post-Form "$base/App/m/leads/create" $s @{ f_name = 'مریم احمدی'; f_company = 'فناوران پارس'; f_phone = '09121234567'; f_status = 'warm'; '__RequestVerificationToken' = (Get-Token $createPage.Content) }
Write-Output "=== create result ==="
Write-Output "final url: $($created.BaseResponse.ResponseUri)"
$err = [regex]::Matches($created.Content, 'invalid-feedback d-block">([^<]+)<')
$err | ForEach-Object { Write-Output "field error: $($_.Groups[1].Value)" }
$alert = [regex]::Matches($created.Content, 'alert alert-\w+[^>]*>\s*([^<]+)')
$alert | ForEach-Object { Write-Output "alert: $($_.Groups[1].Value.Trim())" }
Write-Output "rows:"
[regex]::Matches($created.Content, '<td>([^<]*)</td>') | Select-Object -First 10 | ForEach-Object { Write-Output " cell: '$($_.Groups[1].Value)'" }
