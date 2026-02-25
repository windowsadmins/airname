[CmdletBinding()]
param(
    [switch]$Sign,
    [switch]$NoSign,
    [string]$Thumbprint,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($null -eq $root -or $root -eq '') { $root = $PWD }

$project = Join-Path $root "src" "AirName.csproj"
$publishDir = Join-Path $root "release"

function Find-SigningCert {
    foreach ($store in @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")) {
        $cert = Get-ChildItem $store -ErrorAction SilentlyContinue |
            Where-Object { $_.HasPrivateKey -and $_.Subject -like '*EmilyCarrU*' -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if ($cert) { return $cert }
    }
    return $null
}

# Auto-detect signing cert unless -NoSign
if (-not $NoSign -and [string]::IsNullOrEmpty($Thumbprint)) {
    $cert = Find-SigningCert
    if ($cert) {
        $Thumbprint = $cert.Thumbprint
        Write-Host "Auto-detected certificate: $($cert.Subject)" -ForegroundColor Green
        $Sign = $true
    } elseif ($Sign) {
        throw "No valid code signing certificate found in CurrentUser or LocalMachine stores"
    } else {
        Write-Host "No signing certificate found - binaries will be unsigned" -ForegroundColor Yellow
    }
}

if ($NoSign) { $Sign = $false }

# Resolve signtool.exe
$script:signtool = $null
if ($Sign) {
    $script:signtool = (Get-Command "signtool.exe" -ErrorAction SilentlyContinue).Source
    if (-not $script:signtool) {
        $kitRoots = @("${env:ProgramFiles}\Windows Kits\10\bin", "${env:ProgramFiles(x86)}\Windows Kits\10\bin") |
            Where-Object { Test-Path $_ }
        foreach ($kitRoot in $kitRoots) {
            Get-ChildItem $kitRoot -Directory | Sort-Object Name -Descending | ForEach-Object {
                foreach ($a in @("x64", "arm64")) {
                    $p = Join-Path $_.FullName "$a\signtool.exe"
                    if (!$script:signtool -and (Test-Path $p)) { $script:signtool = $p }
                }
            }
        }
    }
    if (-not $script:signtool) { throw "signtool.exe not found. Install Windows SDK." }
}

Write-Host "Building AirName..." -ForegroundColor Cyan

# Clean
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# Build for both architectures
foreach ($arch in @("x64", "arm64")) {
    $outDir = Join-Path $publishDir $arch
    Write-Host "  Publishing $arch..." -ForegroundColor Gray

    dotnet publish $project `
        -c $Configuration `
        -r "win-$arch" `
        -o $outDir `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) { throw "Build failed for $arch" }

    $exe = Join-Path $outDir "airname.exe"
    if (!(Test-Path $exe)) { throw "Output not found: $exe" }

    if ($Sign) {
        Write-Host "  Signing $arch with $($Thumbprint.Substring(0,8))..." -ForegroundColor Gray
        $signResult = & $script:signtool sign /sha1 $Thumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $exe 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $signResult" }
    }

    $size = [math]::Round((Get-Item $exe).Length / 1KB)
    Write-Host "  $arch done ($size KB)" -ForegroundColor Green
}

Write-Host "`nBuild complete." -ForegroundColor Cyan
Write-Host "  x64:   $(Join-Path $publishDir 'x64' 'airname.exe')"
Write-Host "  arm64: $(Join-Path $publishDir 'arm64' 'airname.exe')"
