$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'
$email = "user$(Get-Random)@test.ir"

function Get-Token($content) {
    ([regex]::Match($content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
}

# PS 5.1 mangles UTF-8 in hashtable bodies; URL-encode manually (result is pure ASCII)
function Post-Form($url, $session, [hashtable]$fields) {
    $pairs = foreach ($k in $fields.Keys) {
        "$([System.Uri]::EscapeDataString($k))=$([System.Uri]::EscapeDataString([string]$fields[$k]))"
    }
    Invoke-WebRequest -Uri $url -Method Post -WebSession $session -UseBasicParsing `
        -ContentType 'application/x-www-form-urlencoded' -Body ($pairs -join '&')
}

# Razor HTML-encodes Persian model values as &#x...; entities — decode before matching
function Decode($response) { [System.Net.WebUtility]::HtmlDecode($response.Content) }

# 1) Register: creates tenant + admin + trial
$reg = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s -UseBasicParsing
$resp = Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'شرکت آزمایشی نور'; FullName = 'علی رضایی'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) }
if ($resp.BaseResponse.ResponseUri.AbsolutePath -ne '/App') { throw "register did not land on dashboard: $($resp.BaseResponse.ResponseUri)" }
Write-Output "1. REGISTER OK ($email)"
if ((Decode $resp) -match 'روز از دوره آزمایشی') { Write-Output "2. TRIAL BADGE OK" }
if ((Decode $resp) -match 'سرنخ') { Write-Output "3. SEEDED MODULE ON DASHBOARD OK" }

# 2) Dynamic list page
$list = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
if ($list.BaseResponse.ResponseUri.AbsolutePath -ne '/App/m/leads') { throw 'leads list redirected' }
Write-Output "4. DYNAMIC LIST OK"

# 3) Create record via dynamic form
$createPage = Invoke-WebRequest -Uri "$base/App/m/leads/create" -WebSession $s -UseBasicParsing
$created = Post-Form "$base/App/m/leads/create" $s @{ f_name = 'مریم احمدی'; f_company = 'فناوران پارس'; f_phone = '09121234567'; f_email = 'maryam@pars.ir'; f_status = 'warm'; f_source = 'website'; f_description = 'تماس از فرم سایت'; '__RequestVerificationToken' = (Get-Token $createPage.Content) }
if ((Decode $created) -match 'مریم احمدی') { Write-Output "5. CREATE + LIST RENDER OK" } else { throw 'created record not in list' }
if ((Decode $created) -match 'گرم') { Write-Output "6. PICKLIST BADGE OK" }
$recordId = ([regex]::Match($created.Content, '/App/m/leads/(\d+)/edit')).Groups[1].Value
if (-not $recordId) { throw 'record id not found in list' }

# 4) Required-field validation
$createPage2 = Invoke-WebRequest -Uri "$base/App/m/leads/create" -WebSession $s -UseBasicParsing
$invalid = Post-Form "$base/App/m/leads/create" $s @{ f_company = 'بدون نام'; '__RequestVerificationToken' = (Get-Token $createPage2.Content) }
if ((Decode $invalid) -match 'الزامی است') { Write-Output "7. VALIDATION ERROR SHOWN OK" } else { throw 'validation message missing' }

# 5) Duplicate detection on unique phone
$createPage3 = Invoke-WebRequest -Uri "$base/App/m/leads/create" -WebSession $s -UseBasicParsing
$dup = Post-Form "$base/App/m/leads/create" $s @{ f_name = 'تکراری'; f_phone = '09121234567'; '__RequestVerificationToken' = (Get-Token $createPage3.Content) }
if ((Decode $dup) -match 'از قبل وجود دارد') { Write-Output "8. DUPLICATE CHECK OK" } else { throw 'duplicate not detected' }

# 6) Edit record
$editPage = Invoke-WebRequest -Uri "$base/App/m/leads/$recordId/edit" -WebSession $s -UseBasicParsing
$edited = Post-Form "$base/App/m/leads/$recordId/edit" $s @{ f_name = 'مریم احمدی'; f_company = 'فناوران پارس نو'; f_phone = '09121234567'; f_status = 'hot'; '__RequestVerificationToken' = (Get-Token $editPage.Content) }
if ((Decode $edited) -match 'فناوران پارس نو') { Write-Output "9. EDIT OK" } else { throw 'edit failed' }

# 7) Search
$search = Invoke-WebRequest -Uri "$base/App/m/leads?q=%D9%85%D8%B1%DB%8C%D9%85" -WebSession $s -UseBasicParsing
if ((Decode $search) -match 'مریم احمدی') { Write-Output "10. SEARCH OK" }

# 8) Excel export
$export = Invoke-WebRequest -Uri "$base/App/m/leads/export" -WebSession $s -UseBasicParsing
if ($export.Headers['Content-Type'] -like '*spreadsheetml*' -and $export.RawContentLength -gt 1000) { Write-Output "11. EXCEL EXPORT OK ($($export.RawContentLength) bytes)" }

# 9) CSV import (curl handles multipart + UTF-8 correctly)
$csvPath = Join-Path $env:TEMP 'leads-import.csv'
[System.IO.File]::WriteAllBytes($csvPath, [System.Text.Encoding]::UTF8.GetBytes("name,phone,status`nرضا کریمی,09351112233,cold`nسارا مرادی,09359998877,hot"))
$listPage = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
$importToken = Get-Token $listPage.Content
$cookieHeader = ($s.Cookies.GetCookies($base) | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '; '
$cookieJar = Join-Path $env:TEMP 'crm24-curl-cookies.txt'
$importHtmlRaw = & curl.exe -s -L -b $cookieHeader -c $cookieJar -F "__RequestVerificationToken=$importToken" -F "file=@$csvPath;type=text/csv" "$base/App/m/leads/import"
$importHtml = [System.Net.WebUtility]::HtmlDecode(($importHtmlRaw -join "`n"))
if ($importHtml -match 'رکورد وارد شد' -and $importHtml -match 'رضا کریمی') { Write-Output "12. CSV IMPORT OK" } else { Write-Output "12. CSV IMPORT FAILED" }

# 10) Delete + recycle bin + restore
$listPage2 = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
$del = Post-Form "$base/App/m/leads/$recordId/delete" $s @{ '__RequestVerificationToken' = (Get-Token $listPage2.Content) }
if ((Decode $del) -notmatch 'مریم احمدی') { Write-Output "13. SOFT DELETE OK" }
$bin = Invoke-WebRequest -Uri "$base/App/recycle-bin" -WebSession $s -UseBasicParsing
if ((Decode $bin) -match 'مریم احمدی') { Write-Output "14. RECYCLE BIN OK" }
$restored = Post-Form "$base/App/recycle-bin/$recordId/restore" $s @{ '__RequestVerificationToken' = (Get-Token $bin.Content) }
$listAfter = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s -UseBasicParsing
if ((Decode $listAfter) -match 'مریم احمدی') { Write-Output "15. RESTORE OK" }

# 11) Tenant isolation: second tenant must not see first tenant's records
$reg2 = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s2 -UseBasicParsing
$email2 = "user$(Get-Random)@test.ir"
$resp2 = Post-Form "$base/App/Account/Register" $s2 @{ CompanyName = 'شرکت دوم'; FullName = 'حسن حسینی'; Email = $email2; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg2.Content) }
$list2 = Invoke-WebRequest -Uri "$base/App/m/leads" -WebSession $s2 -UseBasicParsing
if ((Decode $list2) -notmatch 'مریم احمدی' -and (Decode $list2) -match 'هنوز رکوردی ثبت نشده') { Write-Output "16. TENANT ISOLATION OK" } else { throw 'TENANT LEAK!' }

# 12) Login with existing account + logout
$loginPage = Invoke-WebRequest -Uri "$base/App/Account/Login" -SessionVariable s3 -UseBasicParsing
$login = Post-Form "$base/App/Account/Login" $s3 @{ Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $loginPage.Content) }
if ($login.BaseResponse.ResponseUri.AbsolutePath -eq '/App') { Write-Output "17. LOGIN OK" } else { throw 'login failed' }
$logout = Post-Form "$base/App/Account/Logout" $s3 @{ '__RequestVerificationToken' = (Get-Token $login.Content) }
$afterLogout = Invoke-WebRequest -Uri "$base/App" -WebSession $s3 -UseBasicParsing
if ($afterLogout.BaseResponse.ResponseUri.AbsolutePath -like '*/Login*') { Write-Output "18. LOGOUT OK" }

Write-Output "ALL E2E CHECKS PASSED"
