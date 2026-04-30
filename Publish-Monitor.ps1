#Requires -Version 5.1
# Windows PowerShell 5.1 compatible. -Zip requires Microsoft.PowerShell.Archive (built into Win 10 / Server 2016+).
<#
.SYNOPSIS
  Publishes SyntheticHttpMonitor as a self-contained win-x64 folder (and optionally a release .zip).

.DESCRIPTION
  Publishes the Windows service, the graphical installer (SyntheticHttpMonitor.Setup.exe), and the
  configuration editor (SyntheticHttpMonitor.Config.exe). The Setup project is published as a
  single-file self-contained EXE so the release zip can place SyntheticHttpMonitor.Installer.exe
  at the zip root without splitting its dependencies into Resources.

  Each project is published to its own staging directory first, then files are merged into the
  final output folder. Publishing all three directly to the same -o path can trigger MSB3030 with
  a nested publish\win-x64 path under some SDK / folder layouts.

  -Package copies example JSON files, README.md, roadmap.md, and START_HERE.txt into the publish output
  so you can zip that folder for operators.

  Optional Authenticode signing: use -SignCertificateThumbprint (cert in CurrentUser\My or LocalMachine\My)
  or -SignCertificatePath (PFX). Prefers Windows SDK signtool.exe when present; if signtool is not installed,
  falls back to Set-AuthenticodeSignature (same cmdlet as for .ps1 scripts). Signing runs after publish and
  before -Zip so the archive contains signed binaries. With signtool and a password-protected private key,
  expect one password prompt per file signed (three EXEs by default).

.PARAMETER SignCertificateThumbprint
  SHA1 thumbprint of a code-signing certificate in the certificate store (spaces optional).
  Uses CurrentUser\My by default; add -SignUseMachineStore for LocalMachine\My.

.PARAMETER SignCertificatePath
  Path to a .pfx file instead of a store thumbprint.

.PARAMETER SignCertificatePassword
  Plain-text PFX password when using -SignCertificatePath. Prefer a thumbprint + store cert when possible.

.PARAMETER SignTimestampServer
  RFC3161 timestamp server URL (default: DigiCert).

.PARAMETER SignToolPath
  Full path to signtool.exe if it is not under Program Files (x86)\Windows Kits\10\bin\...

.EXAMPLE
  .\Publish-Monitor.ps1 -OutputPath 'C:\Deploy\SyntheticHttpMonitor'

.EXAMPLE
  .\Publish-Monitor.ps1 -Package -Zip

.EXAMPLE
  .\Publish-Monitor.ps1 -Package -Zip -SignCertificateThumbprint 'ff768047fb24eb88cfb6adc93c674a5cea227248'

.EXAMPLE
  .\Publish-Monitor.ps1 -Package -Zip -SignCertificatePath 'D:\certs\codesign.pfx' -SignCertificatePassword $env:CODE_SIGN_PFX_PASSWORD
#>
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputPath = '',
    [switch]$Package,
    [switch]$Zip,
    [string]$SignCertificateThumbprint = '',
    [string]$SignCertificatePath = '',
    [string]$SignCertificatePassword = '',
    [string]$SignTimestampServer = 'http://timestamp.digicert.com',
    [string]$SignToolPath = '',
    [switch]$SignUseMachineStore
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found on PATH. Install .NET 8 SDK from https://aka.ms/dotnet/download'
}

function Find-SignToolPath {
    if ($SignToolPath) {
        if (-not (Test-Path -LiteralPath $SignToolPath)) {
            throw "SignToolPath not found: $SignToolPath"
        }
        return $SignToolPath
    }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kitsRoot)) {
        return $null
    }
    $dirs = Get-ChildItem -LiteralPath $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+' }
    $sorted = $dirs | Sort-Object -Property { [version]$_.Name } -Descending
    foreach ($d in $sorted) {
        $candidate = Join-Path $d.FullName 'x64\signtool.exe'
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    return $null
}

function Get-PublishMonitorSigningCertificate {
    param(
        [string]$Thumbprint,
        [string]$PfxPath,
        [string]$PfxPassword,
        [switch]$MachineStore
    )

    if ($Thumbprint) {
        $tp = ($Thumbprint -replace '\s', '').ToUpperInvariant()
        $root = if ($MachineStore) { 'Cert:\LocalMachine\My' } else { 'Cert:\CurrentUser\My' }
        $cert = Get-ChildItem -Path $root -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint.ToUpperInvariant() -eq $tp -and $_.HasPrivateKey } |
            Select-Object -First 1
        if (-not $cert) {
            throw "No certificate with private key found under $root matching thumbprint $tp."
        }
        return $cert
    }

    if ($PfxPath) {
        if (-not $PfxPassword) {
            throw 'PFX signing without signtool requires -SignCertificatePassword (or install Windows SDK for signtool.exe).'
        }
        $sec = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
        return Get-PfxCertificate -FilePath $PfxPath -Password $sec -ErrorAction Stop
    }

    throw 'Internal error: Get-PublishMonitorSigningCertificate requires thumbprint or PFX path.'
}

function Get-PublishedExePaths {
    param([Parameter(Mandatory)][string]$PublishRoot)
    # Sign the merged flat publish folder (before building the release zip layout).
    $exeNames = @(
        'SyntheticHttpMonitor.exe',
        'SyntheticHttpMonitor.Config.exe',
        'SyntheticHttpMonitor.Setup.exe'
    )
    $paths = @()
    foreach ($n in $exeNames) {
        $p = Join-Path $PublishRoot $n
        if (Test-Path -LiteralPath $p) {
            $paths += $p
        }
    }
    if ($paths.Count -eq 0) {
        throw "No shipped EXEs found under '$PublishRoot' to sign."
    }
    return $paths
}

function Invoke-MonitorAuthenticodeSignSigntool {
    param(
        [Parameter(Mandatory)][string]$SignTool,
        [Parameter(Mandatory)][string[]]$Files,
        [string]$Thumbprint,
        [string]$PfxPath,
        [string]$PfxPassword,
        [Parameter(Mandatory)][string]$TimestampServer,
        [switch]$MachineStore
    )

    foreach ($file in $Files) {
        $argList = [System.Collections.Generic.List[string]]::new()
        $argList.AddRange([string[]]@('sign', '/v', '/fd', 'sha256', '/tr', $TimestampServer, '/td', 'sha256'))
        if ($Thumbprint) {
            $tp = $Thumbprint -replace '\s', ''
            if ($MachineStore) {
                $argList.Add('/sm') | Out-Null
            }
            $argList.Add('/sha1') | Out-Null
            $argList.Add($tp) | Out-Null
        }
        elseif ($PfxPath) {
            $argList.Add('/f') | Out-Null
            $argList.Add($PfxPath) | Out-Null
            if ($PfxPassword) {
                $argList.Add('/p') | Out-Null
                $argList.Add($PfxPassword) | Out-Null
            }
        }
        else {
            throw 'Internal error: neither thumbprint nor PFX path for signtool signing.'
        }
        $argList.Add($file) | Out-Null

        Write-Host "Signing (signtool): $(Split-Path -Leaf $file)"
        & $SignTool $argList.ToArray()
        if ($LASTEXITCODE -ne 0) {
            throw "signtool.exe failed with exit code $LASTEXITCODE for $file"
        }
    }
}

function Invoke-MonitorAuthenticodeSignSetAuthenticode {
    param(
        [Parameter(Mandatory)][string[]]$Files,
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory)][string]$TimestampServer
    )

    if (-not (Get-Command Set-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
        throw 'Set-AuthenticodeSignature is not available (Microsoft.PowerShell.Security).'
    }

    $cmd = Get-Command Set-AuthenticodeSignature
    $hasIncludeChain = $cmd.Parameters.ContainsKey('IncludeChain')

    foreach ($file in $Files) {
        Write-Host "Signing (Set-AuthenticodeSignature): $(Split-Path -Leaf $file)"
        $params = @{
            FilePath        = $file
            Certificate     = $Certificate
            TimestampServer = $TimestampServer
            HashAlgorithm   = 'SHA256'
        }
        if ($hasIncludeChain) {
            $params['IncludeChain'] = 'All'
        }

        $result = Set-AuthenticodeSignature @params
        if ($result.Status -ne 'Valid') {
            throw "Set-AuthenticodeSignature failed for ${file}: status=$($result.Status); $($result.StatusMessage)"
        }
    }
}

$out = if ($OutputPath) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $here "publish\$Runtime"))
}

$publishProjects = @(
    (Join-Path $here 'SyntheticHttpMonitor.csproj'),
    (Join-Path $here 'SyntheticHttpMonitor.ConfigEditor\SyntheticHttpMonitor.ConfigEditor.csproj'),
    (Join-Path $here 'SyntheticHttpMonitor.Setup\SyntheticHttpMonitor.Setup.csproj')
)

$mergeRoot = Join-Path $here "obj\publish-staging-$Runtime"
if (Test-Path -LiteralPath $mergeRoot) {
    Remove-Item -LiteralPath $mergeRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $mergeRoot -Force | Out-Null

if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Path $out -Force | Out-Null

$pIndex = 0
foreach ($proj in $publishProjects) {
    $pIndex++
    $stageDir = Join-Path $mergeRoot ("p$pIndex")
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
    # Setup must be single-file: the release zip copies only SyntheticHttpMonitor.Installer.exe to the
    # zip root; dependencies cannot stay under Resources or the host exits before showing the form.
    $isSetup = ([System.IO.Path]::GetFileName($proj) -eq 'SyntheticHttpMonitor.Setup.csproj')
    $publishArgs = @(
        'publish', $proj,
        '-c', $Configuration, '-r', $Runtime,
        '--self-contained', 'true',
        '-o', $stageDir
    )
    if ($isSetup) {
        $publishArgs += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
    }
    else {
        $publishArgs += @('-p:PublishSingleFile=false', '-p:IncludeNativeLibrariesForSelfExtract=true')
    }
    Write-Host "dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Merge -> $out"
    Copy-Item -Path (Join-Path $stageDir '*') -Destination $out -Recurse -Force
}

Remove-Item -LiteralPath $mergeRoot -Recurse -Force

if ($Package -or $Zip) {
    $extras = @(
        'START_HERE.txt',
        'appsettings.Example.json',
        'targets.Example.json',
        'logging.Example.json',
        'notifications.Example.json',
        'README.md',
        'roadmap.md'
    )
    foreach ($name in $extras) {
        $src = Join-Path $here $name
        if (Test-Path -LiteralPath $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $out $name) -Force
            Write-Host "Staged: $name"
        }
    }
}

$wantSign = ($SignCertificateThumbprint -ne '') -or ($SignCertificatePath -ne '')
if ($wantSign) {
    if ($SignCertificateThumbprint -ne '' -and $SignCertificatePath -ne '') {
        throw 'Use either -SignCertificateThumbprint or -SignCertificatePath, not both.'
    }
    if ($SignCertificatePath -ne '' -and -not (Test-Path -LiteralPath $SignCertificatePath)) {
        throw "PFX not found: $SignCertificatePath"
    }

    $thumb = $SignCertificateThumbprint
    $pfx = $SignCertificatePath
    $exeFiles = Get-PublishedExePaths -PublishRoot $out

    $signtoolExe = Find-SignToolPath
    if ($signtoolExe) {
        Write-Host "Using signtool: $signtoolExe"
        $n = $exeFiles.Count
        Write-Host "Signing $n EXE(s). If your cert private key is password-protected, you may get $n password prompt(s) in a row (one per file) — same password each time."
        Invoke-MonitorAuthenticodeSignSigntool -SignTool $signtoolExe -Files $exeFiles `
            -Thumbprint $thumb -PfxPath $pfx -PfxPassword $SignCertificatePassword `
            -TimestampServer $SignTimestampServer -MachineStore:$SignUseMachineStore
    }
    else {
        Write-Host 'signtool.exe not found; using Set-AuthenticodeSignature (install Windows SDK later if you prefer signtool).'
        $cert = Get-PublishMonitorSigningCertificate -Thumbprint $thumb -PfxPath $pfx `
            -PfxPassword $SignCertificatePassword -MachineStore:$SignUseMachineStore
        Invoke-MonitorAuthenticodeSignSetAuthenticode -Files $exeFiles -Certificate $cert `
            -TimestampServer $SignTimestampServer
    }
    Write-Host 'Code signing finished.'
}

Write-Host ""
Write-Host "Published to: $out"

if ($Zip) {
    if (-not (Get-Command Compress-Archive -ErrorAction SilentlyContinue)) {
        throw 'Compress-Archive is not available. Install Windows PowerShell 5.1 with the Microsoft.PowerShell.Archive module, or zip the publish folder manually.'
    }

    $releaseFolderName = "SyntheticHttpMonitor-$Runtime"
    $zipRoot = Join-Path $here $releaseFolderName
    if (Test-Path -LiteralPath $zipRoot) {
        Remove-Item -LiteralPath $zipRoot -Recurse -Force
    }
    $resDir = Join-Path $zipRoot 'Resources'
    New-Item -ItemType Directory -Path $resDir -Force | Out-Null
    Copy-Item -Path (Join-Path $out '*') -Destination $resDir -Recurse -Force

    $readmeSrc = Join-Path $here 'README.md'
    if (Test-Path -LiteralPath $readmeSrc) {
        Copy-Item -LiteralPath $readmeSrc -Destination (Join-Path $zipRoot 'readme.md') -Force
        Write-Host "Release layout: readme.md at zip root"
    }

    $setupSrc = Join-Path $out 'SyntheticHttpMonitor.Setup.exe'
    if (-not (Test-Path -LiteralPath $setupSrc)) {
        throw "Expected SyntheticHttpMonitor.Setup.exe under publish output for installer rename: $setupSrc"
    }
    Copy-Item -LiteralPath $setupSrc -Destination (Join-Path $zipRoot 'SyntheticHttpMonitor.Installer.exe') -Force
    Write-Host "Release layout: SyntheticHttpMonitor.Installer.exe at zip root (self-contained single-file Setup publish)"

    # Avoid ~duplicate huge self-contained Setup binary inside Resources (operators use root Installer.exe).
    Remove-Item -LiteralPath (Join-Path $resDir 'SyntheticHttpMonitor.Setup.exe') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $resDir 'SyntheticHttpMonitor.Setup.pdb') -Force -ErrorAction SilentlyContinue

    $zipName = "$releaseFolderName.zip"
    $zipPath = Join-Path $here $zipName
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path $zipRoot -DestinationPath $zipPath -Force
    Remove-Item -LiteralPath $zipRoot -Recurse -Force
    Write-Host "Created: $zipPath"
    Write-Host "Operators: unzip, open readme.md, run SyntheticHttpMonitor.Installer.exe as Administrator (binaries and examples are under Resources)."
}
else {
    Write-Host "Tip: use -Package -Zip for a release zip (readme + Installer at top level, payload under Resources); on the server run the Installer as Administrator."
    if (-not $wantSign) {
        Write-Host "Tip: add -SignCertificateThumbprint <SHA1> (and optional -SignUseMachineStore) before -Zip to Authenticode-sign the shipped EXEs."
    }
}
