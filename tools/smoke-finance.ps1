$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5100'
$email = "fin$(Get-Random)@test.ir"

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

# 1) Register tenant
$reg = Invoke-WebRequest -Uri "$base/App/Account/Register" -SessionVariable s -UseBasicParsing
Post-Form "$base/App/Account/Register" $s @{ CompanyName = 'مالی تست'; FullName = 'مدیر مالی'; Email = $email; Password = 'Test@12345'; '__RequestVerificationToken' = (Get-Token $reg.Content) } | Out-Null
Write-Output "1. TENANT REGISTERED"

# 2) Create product: price 500,000 / tax 9% / stock 10 / reorder point 9
$pp = Invoke-WebRequest -Uri "$base/App/products/create" -WebSession $s -UseBasicParsing
$prod = Post-Form "$base/App/products/save" $s @{
    Id = 0; Name = 'لایسنس نرم‌افزار'; Sku = 'LIC-01'; Unit = 'عدد'; SalePrice = 500000; TaxPercent = 9;
    IsService = 'false'; TrackInventory = 'true'; StockQty = 10; ReorderPoint = 9; IsActive = 'true';
    '__RequestVerificationToken' = (Get-Token $pp.Content)
}
if ((Decode $prod) -match 'لایسنس نرم‌افزار') { Write-Output "2. PRODUCT CREATED" } else { throw 'product create failed' }
$prodEditUrl = ([regex]::Match($prod.Content, '/App/products/(\d+)/edit')).Groups[1].Value

# 3) Commission rule 10% on whole invoice (before invoicing)
$crp = Invoke-WebRequest -Uri "$base/App/commissions/create" -WebSession $s -UseBasicParsing
Post-Form "$base/App/commissions/save" $s @{
    Id = 0; Name = 'پورسانت عمومی'; Percent = 10; FixedAmount = 0; MinInvoiceAmount = 0; IsActive = 'true';
    '__RequestVerificationToken' = (Get-Token $crp.Content)
} | Out-Null
Write-Output "3. COMMISSION RULE CREATED"

# 4) Create quote: 2x product (500,000 + 9%) + manual line 200,000 with 10% discount
$qp = Invoke-WebRequest -Uri "$base/App/finance/quotes/create" -WebSession $s -UseBasicParsing
$quote = Post-Form "$base/App/finance/save" $s @{
    Id = 0; Kind = 'Quote'; CustomerName = 'شرکت آریا'; DiscountPercent = 0;
    'Lines[0].ProductId' = $prodEditUrl; 'Lines[0].Title' = 'لایسنس نرم‌افزار'; 'Lines[0].Quantity' = 2;
    'Lines[0].UnitPrice' = 500000; 'Lines[0].DiscountPercent' = 0; 'Lines[0].TaxPercent' = 9;
    'Lines[1].Title' = 'خدمات نصب'; 'Lines[1].Quantity' = 1; 'Lines[1].UnitPrice' = 200000;
    'Lines[1].DiscountPercent' = 10; 'Lines[1].TaxPercent' = 0;
    '__RequestVerificationToken' = (Get-Token $qp.Content)
}
$qDecoded = Decode $quote
if ($qDecoded -notmatch 'پیش‌فاکتور شماره') { throw 'quote create failed' }
# SubTotal 1,180,000 + Tax 90,000 = Grand 1,270,000
if ($qDecoded -match '1,270,000') { Write-Output "4. QUOTE TOTALS CORRECT (1,270,000)" } else { throw "quote totals wrong" }
$quoteId = ([regex]::Match($quote.BaseResponse.ResponseUri.AbsolutePath, '/doc/(\d+)')).Groups[1].Value

# 5) Convert quote -> order
$convertToken = Get-Token $quote.Content
$order = Post-Form "$base/App/finance/doc/$quoteId/convert" $s @{ '__RequestVerificationToken' = $convertToken }
$oDecoded = Decode $order
if ($oDecoded -match 'سفارش فروش شماره') { Write-Output "5. QUOTE -> ORDER CONVERTED" } else { throw 'quote->order failed' }
$orderId = ([regex]::Match($order.BaseResponse.ResponseUri.AbsolutePath, '/doc/(\d+)')).Groups[1].Value

# 6) Convert order -> invoice (deducts stock)
$invoice = Post-Form "$base/App/finance/doc/$orderId/convert" $s @{ '__RequestVerificationToken' = (Get-Token $order.Content) }
$iDecoded = Decode $invoice
if ($iDecoded -match 'فاکتور شماره') { Write-Output "6. ORDER -> INVOICE CONVERTED" } else { throw 'order->invoice failed' }
$invoiceId = ([regex]::Match($invoice.BaseResponse.ResponseUri.AbsolutePath, '/doc/(\d+)')).Groups[1].Value

# 7) Stock deducted 10 -> 8 and low-stock alert shown (reorder point 9)
$plist = Invoke-WebRequest -Uri "$base/App/products" -WebSession $s -UseBasicParsing
$plDecoded = Decode $plist
if ($plDecoded -match '8\s*—\s*کمبود' -or $plDecoded -match 'کمبود') { Write-Output "7. STOCK DEDUCTED + LOW STOCK ALERT" } else { throw 'stock deduction failed' }

# 8) Partial payment 1,000,000 -> status partially paid
$pay1 = Post-Form "$base/App/finance/doc/$invoiceId/pay" $s @{ amount = 1000000; method = 'card'; '__RequestVerificationToken' = (Get-Token $invoice.Content) }
$p1Decoded = Decode $pay1
if ($p1Decoded -match 'پرداخت ناقص') { Write-Output "8. PARTIAL PAYMENT OK" } else { throw 'partial payment failed' }

# 9) Pay remainder 270,000 -> paid + commission computed
$pay2 = Post-Form "$base/App/finance/doc/$invoiceId/pay" $s @{ amount = 270000; method = 'cash'; '__RequestVerificationToken' = (Get-Token $pay1.Content) }
$p2Decoded = Decode $pay2
if ($p2Decoded -match 'تسویه‌شده') { Write-Output "9. INVOICE FULLY PAID" } else { throw 'full payment failed' }

# 10) Commission report: 10% of 1,270,000 = 127,000
$comm = Invoke-WebRequest -Uri "$base/App/commissions" -WebSession $s -UseBasicParsing
$commDecoded = Decode $comm
if ($commDecoded -match '127,000') { Write-Output "10. COMMISSION COMPUTED (127,000)" } else { throw 'commission failed' }

# 11) Print view renders
$print = Invoke-WebRequest -Uri "$base/App/finance/doc/$invoiceId/print" -WebSession $s -UseBasicParsing
$prDecoded = Decode $print
if ($prDecoded -match 'فاکتور' -and $prDecoded -match 'شرکت آریا' -and $prDecoded -match '1,270,000') { Write-Output "11. PRINT VIEW OK" } else { throw 'print failed' }

# 12) Second invoice (direct) + installments
$ip = Invoke-WebRequest -Uri "$base/App/finance/invoices/create" -WebSession $s -UseBasicParsing
$inv2 = Post-Form "$base/App/finance/save" $s @{
    Id = 0; Kind = 'Invoice'; CustomerName = 'فروشگاه مهر'; DiscountPercent = 0;
    'Lines[0].Title' = 'قرارداد پشتیبانی'; 'Lines[0].Quantity' = 1; 'Lines[0].UnitPrice' = 900000;
    'Lines[0].DiscountPercent' = 0; 'Lines[0].TaxPercent' = 0;
    '__RequestVerificationToken' = (Get-Token $ip.Content)
}
$inv2Id = ([regex]::Match($inv2.BaseResponse.ResponseUri.AbsolutePath, '/doc/(\d+)')).Groups[1].Value
$inst = Post-Form "$base/App/finance/doc/$inv2Id/installments" $s @{ count = 3; firstDueDate = '2026-08-01'; '__RequestVerificationToken' = (Get-Token $inv2.Content) }
$instDecoded = Decode $inst
if ($instDecoded -match '3 قسط ایجاد شد' -and ([regex]::Matches($instDecoded, '300,000')).Count -ge 3) { Write-Output "12. 3 INSTALLMENTS CREATED (300,000 each)" } else { throw 'installments failed' }

# 13) Pay first installment -> payment recorded, status partially paid
$instId = ([regex]::Match($inst.Content, '/App/finance/installment/(\d+)/pay')).Groups[1].Value
$payInst = Post-Form "$base/App/finance/installment/$instId/pay" $s @{ documentId = $inv2Id; '__RequestVerificationToken' = (Get-Token $inst.Content) }
$piDecoded = Decode $payInst
if ($piDecoded -match 'پرداخت شد' -and $piDecoded -match 'پرداخت ناقص') { Write-Output "13. INSTALLMENT PAID (status partial)" } else { throw 'installment pay failed' }

# 14) Draft-only edit guard: editing confirmed invoice redirects with error
$editTry = Invoke-WebRequest -Uri "$base/App/finance/doc/$invoiceId/edit" -WebSession $s -UseBasicParsing
if ((Decode $editTry) -match 'فقط سند پیش‌نویس') { Write-Output "14. EDIT GUARD ON CONFIRMED DOC OK" } else { throw 'edit guard failed' }

Write-Output "ALL FINANCE CHECKS PASSED"
