[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\data\products.snapshot.json'),
    [switch]$Refresh
)

$ErrorActionPreference = 'Stop'
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ((Test-Path -LiteralPath $OutputPath) -and -not $Refresh) {
    Write-Output "Snapshot already exists: $OutputPath. Use -Refresh to explicitly replace it."
    exit 0
}

$source = 'https://dummyjson.com/products?limit=0&select=id,title,description,category,price,stock,brand,sku,thumbnail,images,tags'
$response = Invoke-RestMethod -Uri $source -TimeoutSec 30
$categories = @('mens-shirts', 'mens-shoes', 'womens-dresses', 'womens-shoes', 'womens-bags', 'tops', 'sunglasses', 'womens-jewellery')
$selected = @($response.products | Where-Object { $_.category -in $categories } | Sort-Object id)
if ($selected.Count -lt 30) {
    throw "Import returned only $($selected.Count) clothing/accessory products. At least 30 are required."
}

$products = @(
    foreach ($product in $selected) {
        if (-not $product.id -or -not $product.title -or $product.price -lt 0) {
            throw "Invalid public product record."
        }
        $images = @($product.images | ForEach-Object {
            $uri = [Uri]$_
            if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'cdn.dummyjson.com') {
                throw "Unexpected image host for product $($product.id)."
            }
            $uri.AbsoluteUri
        })
        $thumbnail = [Uri]$product.thumbnail
        if ($thumbnail.Scheme -ne 'https' -or $thumbnail.Host -ne 'cdn.dummyjson.com') {
            throw "Unexpected thumbnail host for product $($product.id)."
        }
        [ordered]@{
            id = [int]$product.id
            title = [string]$product.title
            description = [string]$product.description
            category = [string]$product.category
            price = [decimal]$product.price
            currency = 'USD'
            stock = [int]$product.stock
            brand = $product.brand
            sku = [string]$product.sku
            thumbnail = $thumbnail.AbsoluteUri
            images = $images
            tags = @($product.tags)
        }
    }
)

$main = $products | Where-Object { $_.id -eq 83 }
if (-not $main -or $main.price -ne [decimal]29.99) {
    throw 'The public fixture product 83 changed. Review the scenario before replacing the snapshot.'
}

$canonical = ConvertTo-Json -InputObject $products -Depth 12 -Compress
$hashBytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($canonical))
$hash = [Convert]::ToHexString($hashBytes).ToLowerInvariant()
$snapshot = [ordered]@{
    source = 'DummyJSON'
    sourceUrl = $source
    retrievedAt = [DateTimeOffset]::UtcNow.ToString('O')
    contentHash = $hash
    hashScope = 'SHA-256 of imported products serialized as compact JSON by PowerShell'
    notice = 'Public sample catalog for prototyping. USD is the demo convention; prices and stock are not real commerce data. Image URLs are UI-only; image files are not redistributed.'
    products = $products
}
$directory = [System.IO.Path]::GetDirectoryName($OutputPath)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$temporary = "$OutputPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    [System.IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $snapshot -Depth 14), [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
} finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary
    }
}
Write-Output "Imported $($products.Count) products. Source: DummyJSON. Images: URLs only. Snapshot: $OutputPath"
Write-Output "Content hash: $hash"
