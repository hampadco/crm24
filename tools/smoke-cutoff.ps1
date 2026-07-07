$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'

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
function Decode($response) { [System.Net.WebUtility]::HtmlDecode($response.Content) }

# New tenant
$email = "cutoff$(Get-Random)@test.ir"
$reg = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable u -UseBasicParsing
$resp = Post-Form "$base/App/Account/Register" $u @{ CompanyName = 'شرکت قطع دسترسی'; FullName = 'کاربر تست'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) }
if ($resp.BaseResponse.ResponseUri.AbsolutePath -ne '/App') { throw 'register failed' }
Write-Output "1. TENANT REGISTERED"

# Owner suspends it
$loginPage = Invoke-WebRequest -Uri "$base/Admin/Account/Login" -SessionVariable o -UseBasicParsing
Post-Form "$base/Admin/Account/Login" $o @{ Username = 'admin'; Password = 'Crm24@1405'; '__RequestVerificationToken' = (Get-Token $loginPage.Content) } | Out-Null
$tenants = Invoke-WebRequest -Uri "$base/Owner/Tenants?q=$([System.Uri]::EscapeDataString('قطع دسترسی'))" -WebSession $o -UseBasicParsing
$tenantId = ([regex]::Match($tenants.Content, '/Owner/Tenants/Details/(\d+)')).Groups[1].Value
if (-not $tenantId) { throw 'tenant not found in owner list' }
$details = Invoke-WebRequest -Uri "$base/Owner/Tenants/Details/$tenantId" -WebSession $o -UseBasicParsing
Post-Form "$base/Owner/Tenants/SetStatus/$tenantId" $o @{ status = 'Suspended'; '__RequestVerificationToken' = (Get-Token $details.Content) } | Out-Null
Write-Output "2. TENANT SUSPENDED BY OWNER"

# Wait out the 60s status cache? New cache entry per tenant id was created during register request (dashboard).
# The filter caches for 60s; sleep a bit over.
Start-Sleep -Seconds 61

# Tenant user is now blocked
$blocked = Invoke-WebRequest -Uri "$base/App" -WebSession $u -UseBasicParsing
if ($blocked.BaseResponse.ResponseUri.AbsolutePath -like '*Expired*' -and (Decode $blocked) -match 'به پایان رسیده') {
    Write-Output "3. ACCESS CUTOFF OK (redirected to Expired)"
} else {
    throw "cutoff failed: $($blocked.BaseResponse.ResponseUri)"
}

Write-Output "CUTOFF CHECK PASSED"
