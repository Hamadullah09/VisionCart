<#
.SYNOPSIS
    Builds a deployable VisionCart package for IIS / myASP.NET.

.DESCRIPTION
    Produces a self-contained folder you can upload over FTP or copy into an IIS
    site. Nothing here needs Docker, Node.js on the server, or a build step at
    the destination — the target only has to run the .NET hosting bundle.

    Client-side assets are built here, not on the server. Node is a build-time
    dependency only; a shared host has no npm.

.PARAMETER Output
    Where to write the package. Defaults to .\publish.

.PARAMETER SkipTests
    Skip the test run. Use only when you have just run them.

.PARAMETER Runtime
    Target runtime. win-x64 suits any modern IIS app pool; use win-x86 only if
    the host's pool is set to 32-bit ("Enable 32-Bit Applications" = True).

    This matters more than it looks: SkiaSharp ships native binaries for every
    platform it supports, so an untargeted publish carries ~119 MB of Linux,
    macOS and ARM libraries that IIS will never load. On a shared plan that is
    most of a disk quota and most of an FTP upload.
#>
[CmdletBinding()]
param(
    [string] $Output = "publish",
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string] $Runtime = "win-x64",
    [switch] $SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$web  = Join-Path $root "src\VisionCart.Web"

Write-Host "VisionCart — production package" -ForegroundColor Cyan
Write-Host ("-" * 50)

# --- 1. Tests -----------------------------------------------------------------
# A package that has not been tested is not a release candidate. This is opt-out
# rather than opt-in on purpose.
if (-not $SkipTests) {
    Write-Host "`n[1/4] Running tests..." -ForegroundColor Yellow
    dotnet test $root --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. The package was not built." }
} else {
    Write-Host "`n[1/4] Tests skipped." -ForegroundColor DarkYellow
}

# --- 2. Client assets ---------------------------------------------------------
Write-Host "`n[2/4] Building client assets..." -ForegroundColor Yellow
$clientApp = Join-Path $web "ClientApp"

if (Test-Path (Join-Path $clientApp "package.json")) {
    Push-Location $clientApp
    try {
        if (-not (Test-Path "node_modules")) { npm ci }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "The client asset build failed." }
    } finally { Pop-Location }
} else {
    Write-Host "  No ClientApp build configured; using the committed bundle." -ForegroundColor DarkGray
}

# --- 3. Publish ---------------------------------------------------------------
Write-Host "`n[3/4] Publishing..." -ForegroundColor Yellow

$target = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
if (Test-Path $target) { Remove-Item $target -Recurse -Force }

# --self-contained false keeps this framework-dependent: the host supplies the
# .NET runtime through the hosting bundle, and the package stays small.
dotnet publish $web `
    --configuration Release `
    --runtime $Runtime `
    --self-contained false `
    --output $target `
    --nologo `
    /p:EnvironmentName=Production

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# --- 4. Strip anything that must not be uploaded ------------------------------
Write-Host "`n[4/4] Removing files that must not ship..." -ForegroundColor Yellow

# The development settings carry a LocalDB connection string and the seeded demo
# passwords. Uploading them would put both on a public server.
$forbidden = @(
    "appsettings.Development.json",
    "*.pdb",

    # The publish step precompresses static assets, but those variants are only
    # served by MapStaticAssets, and this application uses UseStaticFiles so it
    # can register the .task and .wasm content types the try-on needs. Nothing
    # reads them, and they are ~10 MB of a shared plan's quota. IIS does static
    # compression itself (see urlCompression in web.config).
    "*.wasm.gz",
    "*.js.gz",
    "*.css.gz",
    "*.br"
)

foreach ($pattern in $forbidden) {
    Get-ChildItem -Path $target -Filter $pattern -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host ("  removed {0}" -f $_.Name) -ForegroundColor DarkGray
            Remove-Item $_.FullName -Force
        }
}

# The uploads folder must exist and must be writable by the application pool,
# or the first image upload fails with a permissions error.
$uploads = Join-Path $target "wwwroot\uploads"
if (-not (Test-Path $uploads)) { New-Item -ItemType Directory -Path $uploads | Out-Null }
$logs = Join-Path $target "logs"
if (-not (Test-Path $logs)) { New-Item -ItemType Directory -Path $logs | Out-Null }

# --- Report -------------------------------------------------------------------
$size = (Get-ChildItem $target -Recurse -File | Measure-Object -Property Length -Sum).Sum
$files = (Get-ChildItem $target -Recurse -File | Measure-Object).Count

Write-Host "`nPackage ready." -ForegroundColor Green
Write-Host ("  Location : {0}" -f $target)
Write-Host ("  Files    : {0}" -f $files)
Write-Host ("  Size     : {0:N1} MB" -f ($size / 1MB))
Write-Host ("  Runtime  : {0}" -f $Runtime)

Write-Host "`nBefore this will start, set on the host:" -ForegroundColor Cyan
@(
    "ConnectionStrings__DefaultConnection",
    "AllowedHosts",
    "Email__Host / Email__Username / Email__Password",
    "Store__AppUrl"
) | ForEach-Object { Write-Host ("  - {0}" -f $_) }

Write-Host "`nSee docs/07-deployment.md for the full procedure." -ForegroundColor Cyan
