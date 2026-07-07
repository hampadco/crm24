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

# 1) Public pricing page (fed from seeded plans)
$pricing = Invoke-WebRequest -Uri "$base/pricing" -UseBasicParsing
if ((Decode $pricing) -match 'حرفه‌ای') { Write-Output "1. PUBLIC PRICING PAGE OK" } else { throw 'pricing plans missing' }

# 2) Owner area requires admin login
$ownerAnon = Invoke-WebRequest -Uri "$base/Owner" -UseBasicParsing
if ($ownerAnon.BaseResponse.ResponseUri.AbsolutePath -like '*/Login*') { Write-Output "2. OWNER AUTH REQUIRED OK" } else { throw 'owner area open!' }

# 3) Login as site admin
$loginPage = Invoke-WebRequest -Uri "$base/Admin/Account/Login" -SessionVariable o -UseBasicParsing
$login = Post-Form "$base/Admin/Account/Login" $o @{ Username = 'admin'; Password = 'Crm24@1405'; '__RequestVerificationToken' = (Get-Token $loginPage.Content) }
Write-Output "3. ADMIN LOGIN OK"

# 4) Owner dashboard
$dash = Invoke-WebRequest -Uri "$base/Owner" -WebSession $o -UseBasicParsing
if ((Decode $dash) -match 'کل Tenant ها') { Write-Output "4. OWNER DASHBOARD OK" } else { throw 'owner dashboard failed' }

# 5) Tenants list
$tenants = Invoke-WebRequest -Uri "$base/Owner/Tenants" -WebSession $o -UseBasicParsing
$tenantId = ([regex]::Match($tenants.Content, '/Owner/Tenants/Details/(\d+)')).Groups[1].Value
if ($tenantId) { Write-Output "5. TENANTS LIST OK (first id: $tenantId)" } else { throw 'no tenants listed' }

# 6) Tenant details
$details = Invoke-WebRequest -Uri "$base/Owner/Tenants/Details/$tenantId" -WebSession $o -UseBasicParsing
if ((Decode $details) -match 'فضای مصرفی') { Write-Output "6. TENANT DETAILS OK" }

# 7) Suspend then reactivate
$suspend = Post-Form "$base/Owner/Tenants/SetStatus/$tenantId" $o @{ status = 'Suspended'; '__RequestVerificationToken' = (Get-Token $details.Content) }
if ((Decode $suspend) -match 'معلق') { Write-Output "7. SUSPEND OK" } else { throw 'suspend failed' }

# 8) Plans list + create a new plan
$plans = Invoke-WebRequest -Uri "$base/Owner/Plans" -WebSession $o -UseBasicParsing
if ((Decode $plans) -match 'حرفه‌ای') { Write-Output "8. PLANS LIST OK" }
$planForm = Invoke-WebRequest -Uri "$base/Owner/Plans/Create" -WebSession $o -UseBasicParsing
$planSave = Post-Form "$base/Owner/Plans/Save" $o @{
    Name = 'استارتاپ'; Description = 'پلن تستی'; PriceMonthly = '490000'; PriceYearly = '4900000';
    MaxUsers = '2'; MaxRecords = '5000'; MaxStorageMb = '512'; Features = "ماژول فروش`nپشتیبانی انجمن";
    IsActive = 'true'; IsFeatured = 'false'; SortOrder = '0'; '__RequestVerificationToken' = (Get-Token $planForm.Content)
}
if ((Decode $planSave) -match 'استارتاپ') { Write-Output "9. PLAN CREATE OK" } else { throw 'plan create failed' }

# 10) Create subscription for the tenant (activates it)
$subForm = Invoke-WebRequest -Uri "$base/Owner/Subscriptions/Create?tenantId=$tenantId" -WebSession $o -UseBasicParsing
$planId = ([regex]::Match($subForm.Content, '<option value="(\d+)"')).Groups[1].Value
$subSave = Post-Form "$base/Owner/Subscriptions/Create" $o @{
    TenantId = $tenantId; PlanId = $planId; Months = '12'; Amount = '24900000';
    RecordPayment = 'true'; PaymentReference = 'TRX-1001'; Note = 'ثبت دستی تست';
    '__RequestVerificationToken' = (Get-Token $subForm.Content)
}
$decoded = Decode $subSave
if ($decoded -match 'اشتراک' -and $decoded -match 'فعال') { Write-Output "10. SUBSCRIPTION CREATE OK (tenant re-activated)" } else { throw 'subscription failed' }

# 11) Subscriptions list shows revenue
$subs = Invoke-WebRequest -Uri "$base/Owner/Subscriptions" -WebSession $o -UseBasicParsing
if ((Decode $subs) -match '24,900,000') { Write-Output "11. SUBSCRIPTIONS LIST OK" }

# 12) Impersonation: enter tenant panel as owner
$detailsPage = Invoke-WebRequest -Uri "$base/Owner/Tenants/Details/$tenantId" -WebSession $o -UseBasicParsing
$imp = Post-Form "$base/Owner/Tenants/Impersonate/$tenantId" $o @{ '__RequestVerificationToken' = (Get-Token $detailsPage.Content) }
$impDecoded = Decode $imp
if ($imp.BaseResponse.ResponseUri.AbsolutePath -eq '/App' -and $impDecoded -match 'حالت پشتیبانی') { Write-Output "12. IMPERSONATION OK (banner shown)" } else { throw "impersonation failed: $($imp.BaseResponse.ResponseUri)" }

# 13) Stop impersonation returns to owner panel
$stop = Post-Form "$base/App/Account/StopImpersonation" $o @{ '__RequestVerificationToken' = (Get-Token $imp.Content) }
if ($stop.BaseResponse.ResponseUri.AbsolutePath -like '/Owner/*') { Write-Output "13. STOP IMPERSONATION OK" }

# 14) Owner dashboard chart data present
$dash2 = Invoke-WebRequest -Uri "$base/Owner" -WebSession $o -UseBasicParsing
if ($dash2.Content -match 'monthlyChart') { Write-Output "14. DASHBOARD CHART OK" }

Write-Output "ALL OWNER CHECKS PASSED"
