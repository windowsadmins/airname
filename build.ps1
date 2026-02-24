[CmdletBinding()]
param(
    [switch]$Sign,
    [string]$Thumbprint,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($PSScriptRoot -eq $null) { $root = $PWD }

$project = Join-Path $root "src" "AirName.csproj"
$publishDir = Join-Path $root "release"

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
        --self-contained false `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) { throw "Build failed for $arch" }

    $exe = Join-Path $outDir "airname.exe"
    if (!(Test-Path $exe)) { throw "Output not found: $exe" }

    if ($Sign) {
        if ([string]::IsNullOrEmpty($Thumbprint)) {
            $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
                    Where-Object { $_.NotAfter -gt (Get-Date) } |
                    Sort-Object NotAfter -Descending |
                    Select-Object -First 1
            if (!$cert) { throw "No valid code signing certificate found" }
            $Thumbprint = $cert.Thumbprint
        }

        Write-Host "  Signing $arch with $($Thumbprint.Substring(0,8))..." -ForegroundColor Gray
        $signResult = & signtool sign /sha1 $Thumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $exe 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $signResult" }
    }

    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "  $arch done ($size MB)" -ForegroundColor Green
}

Write-Host "`nBuild complete." -ForegroundColor Cyan
Write-Host "  x64:   $(Join-Path $publishDir 'x64' 'airname.exe')"
Write-Host "  arm64: $(Join-Path $publishDir 'arm64' 'airname.exe')"
