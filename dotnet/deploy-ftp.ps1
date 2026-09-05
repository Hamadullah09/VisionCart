<#
.SYNOPSIS
    Uploads a built VisionCart package to a shared Windows host over FTP.

.DESCRIPTION
    Companion to publish.ps1: that builds .\publish, this puts it on the server.

    The password is never a parameter and never appears in a command line, a
    transcript or your shell history. It is read from the VISIONCART_FTP_PASSWORD
    environment variable, which you set yourself:

        $env:VISIONCART_FTP_PASSWORD = 'the FTP account password'

    Set that way it lives only in the shell you set it in and disappears when you
    close it, which is what you want. `setx VISIONCART_FTP_PASSWORD "..."` also
    works and is what you need when another process has to run this script, but
    it writes the password into your Windows user profile until you clear it
    with `setx VISIONCART_FTP_PASSWORD ""`.

    Never put a real password in this file. It is in source control.

.PARAMETER FtpHost
    The host from the control panel, e.g. win8238.site4now.net.

.PARAMETER User
    The FTP account name.

.PARAMETER RemoteRoot
    Directory on the server that becomes the site root. An FTP account scoped to
    one site lands there already, so "/" is usually right; a whole-account login
    needs something like /www/<site>.

.PARAMETER Source
    The package to upload. Defaults to .\publish.

.PARAMETER Include
    Upload these files even though they are excluded by default. Pass web.config
    only when you intend to replace the server's copy.

.PARAMETER NoOffline
    Skip the app_offline.htm step. IIS keeps the application's DLLs open while
    the pool is running, so without it an upload over the top of them fails with
    FTP 550 partway through, leaving the site half-updated. Only pass this when
    the application is already stopped.

.PARAMETER Insecure
    Send credentials over plain FTP. The default requires explicit FTPS
    (AUTH TLS); without it the password crosses the network in clear text.

.PARAMETER WhatIf
    List what would be uploaded and stop.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $FtpHost,
    [Parameter(Mandatory)] [string] $User,
    [string] $RemoteRoot = "/",
    [string] $Source = "publish",
    [string[]] $Include = @(),
    [switch] $NoOffline,
    [switch] $Insecure
)

$ErrorActionPreference = "Stop"

$password = $env:VISIONCART_FTP_PASSWORD
if ([string]::IsNullOrWhiteSpace($password)) {
    throw "VISIONCART_FTP_PASSWORD is not set. See the help at the top of this file; " +
          "the password is deliberately not a parameter."
}

$root = if ([System.IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $PSScriptRoot $Source }
if (-not (Test-Path -LiteralPath $root)) { throw "No package at $root. Run .\publish.ps1 first." }

# web.config is the one file the server owns rather than the package.
#
# On a shared host it carries what the build cannot know: the connection string
# and other secrets in <environmentVariables>, and a hostingModel the platform
# may dictate. Publishing writes a generic one, so uploading it silently replaces
# a working configuration with an empty one — and the failure appears minutes
# later as an unreadable startup error. Opt in with -Include web.config when
# replacing it is genuinely what you want.
$protected = @("web.config")
$skip = $protected | Where-Object { $_ -notin $Include }

$files = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object { $_.Name -notin $skip }

if ($files.Count -eq 0) { throw "$root is empty." }

$totalMb = ($files | Measure-Object -Property Length -Sum).Sum / 1MB
$base = $RemoteRoot.TrimEnd("/")
$log = Join-Path $PSScriptRoot "deploy-ftp.log"

Write-Host "VisionCart - FTP deployment" -ForegroundColor Cyan
Write-Host ("-" * 50)
Write-Host ("  Source : {0}" -f $root)
Write-Host ("  Target : ftp://{0}{1}" -f $FtpHost, $base)
Write-Host ("  Files  : {0}  ({1:N1} MB)" -f $files.Count, $totalMb)
Write-Host ("  TLS    : {0}" -f $(if ($Insecure) { "NO - password sent in clear" } else { "required (AUTH TLS)" }))
if ($skip) { Write-Host ("  Kept   : {0} (server's copy preserved; -Include to overwrite)" -f ($skip -join ", ")) -ForegroundColor DarkYellow }

if ($WhatIfPreference) {
    $files | ForEach-Object { Write-Host ("  would upload {0}" -f $_.FullName.Substring($root.Length + 1)) }
    return
}

# Inside double quotes a curl config file treats backslash as an escape
# character, so a Windows path written verbatim is mangled and the whole file is
# rejected at line 1. Local paths therefore go in with forward slashes, which
# curl accepts on Windows, and anything that could still contain a backslash or a
# quote is escaped explicitly.
$esc = { param($v) $v -replace '\\', '\\' -replace '"', '\"' }

# The options every call shares. Credentials go through a config file rather than
# the command line so they never reach a process list or a shell transcript.
function Get-CurlHeader {
    $h = [System.Collections.Generic.List[string]]::new()
    $h.Add("--user `"$(& $esc $User):$(& $esc $password)`"")
    $h.Add("--ftp-create-dirs")
    $h.Add("--fail")
    $h.Add("--show-error")
    $h.Add("--silent")
    $h.Add("--globoff")           # filenames may contain [ ] { }
    $h.Add("--connect-timeout 30")
    # One line per transfer, so a failure part-way through a 161-file run says
    # which file it died on instead of only that it died.
    $h.Add("--write-out `"%{response_code} %{url_effective}\n`"")
    if (-not $Insecure) { $h.Add("--ssl-reqd") }

    # The comma matters. `return $h` unrolls the List into the pipeline and the
    # caller receives a fixed-size Object[], so the next .Add() throws
    # "Collection was of a fixed size."
    return ,$h
}

# Run curl with a generated config. Native stderr must not become a terminating
# error before the output is captured, or the one thing that says which file
# failed is lost — so the preference is relaxed around the call and restored.
function Invoke-Curl {
    param([string[]] $CurlArgs, [string] $What, [string] $LogTo)

    $cfg = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllLines($cfg, (Get-CurlHeader), [System.Text.UTF8Encoding]::new($false))

        $prev = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $out = & curl.exe --config $cfg @CurlArgs 2>&1
        $rc = $LASTEXITCODE
        $ErrorActionPreference = $prev

        if ($LogTo) { $out | Set-Content -LiteralPath $LogTo }

        if ($rc -ne 0) {
            $clean = ($out -join " ") -replace [regex]::Escape($password), "********"
            throw "Could not $What (curl exit $rc): $clean"
        }
    }
    finally {
        # The config holds the password. It goes even if the call threw.
        if (Test-Path -LiteralPath $cfg) { Remove-Item -LiteralPath $cfg -Force -ErrorAction SilentlyContinue }
    }
}

# IIS holds the application's DLLs open while the pool is running, and uploading
# over the top of them fails with FTP 550 partway through. app_offline.htm is the
# supported way out: IIS sees the file, shuts the application down, releases the
# handles, and serves that page to visitors until it is removed.
if (-not $NoOffline) {
    Write-Host "`nTaking the application offline..." -ForegroundColor Yellow
    $offline = Join-Path ([System.IO.Path]::GetTempPath()) "app_offline.htm"
    Set-Content -LiteralPath $offline -Encoding UTF8 -Value @(
        "<!doctype html><title>Updating</title>"
        "<h1>This shop is being updated</h1>"
        "<p>It will be back in a few minutes.</p>"
    )
    try {
        Invoke-Curl @("--upload-file", $offline.Replace("\", "/"),
                      "--url", "ftp://$FtpHost$base/app_offline.htm") "upload app_offline.htm"
    }
    finally {
        Remove-Item -LiteralPath $offline -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 10   # let IIS drain and release the file handles
}

# curl reuses one control connection across every upload in a single invocation,
# so the whole transfer costs one login rather than 161. The arguments go in a
# file because the list is far longer than a command line may be.
$argFile = [System.IO.Path]::GetTempFileName()

try {
    $lines = Get-CurlHeader
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace("\", "/")
        $local    = $file.FullName.Replace("\", "/")
        $lines.Add("--upload-file `"$local`"")
        $lines.Add("--url `"ftp://$FtpHost$base/$relative`"")
    }

    # No BOM: curl reads the config as bytes, and a BOM becomes part of the first
    # option name.
    [System.IO.File]::WriteAllLines($argFile, $lines, [System.Text.UTF8Encoding]::new($false))

    Write-Host "Uploading..." -ForegroundColor Yellow
    $sw = [Diagnostics.Stopwatch]::StartNew()

    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $out = & curl.exe --config $argFile 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev
    $sw.Stop()

    # curl quotes the failing --url back, and those lines carry the credentials.
    ($out -join [Environment]::NewLine) -replace [regex]::Escape($password), "********" |
        Set-Content -LiteralPath $log

    if ($code -ne 0) {
        throw "curl exited $code. The site may be half-updated and app_offline.htm " +
              "is probably still in place. See $log (scrubbed of credentials)."
    }

    Write-Host ("`nUploaded {0} files in {1:N0}s." -f $files.Count, $sw.Elapsed.TotalSeconds) -ForegroundColor Green

    if (-not $NoOffline) {
        Write-Host "Bringing the application back online..." -ForegroundColor Yellow
        Invoke-Curl @("--quote", "DELE app_offline.htm",
                      "--url", "ftp://$FtpHost$base/") "remove app_offline.htm"
    }

    Write-Host "`nStill to check:" -ForegroundColor Cyan
    Write-Host "  - GET /health/live  expects 200 Healthy"
    Write-Host "  - GET /health/ready expects database: Healthy"
    Write-Host "  - wwwroot/uploads and logs must be writable by the app pool"
}
finally {
    if (Test-Path -LiteralPath $argFile) {
        Remove-Item -LiteralPath $argFile -Force -ErrorAction SilentlyContinue
    }
}
