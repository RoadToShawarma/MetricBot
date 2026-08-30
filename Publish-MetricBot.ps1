[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$releaseRoot = Join-Path $projectRoot 'publish\Release'
$stagingRoot = Join-Path $projectRoot 'publish\.staging'
$appProject = Join-Path $projectRoot 'MetricBot.csproj'

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$targets = @(
    @{ Edition = 'Modern'; Framework = 'net10.0-windows'; Runtime = 'win-x64' },
    @{ Edition = 'Modern'; Framework = 'net10.0-windows'; Runtime = 'win-x86' },
    @{ Edition = 'Legacy'; Framework = 'net6.0-windows'; Runtime = 'win-x64' },
    @{ Edition = 'Legacy'; Framework = 'net6.0-windows'; Runtime = 'win-x86' }
)

try {
    foreach ($target in $targets) {
        $packageName = "MetricBot-$($target.Edition)-$($target.Runtime)"
        $packageRoot = Join-Path $stagingRoot $packageName
        $archivePath = Join-Path $releaseRoot "$packageName.zip"

        Write-Host "Publishing $packageName..."
        dotnet publish $appProject -c $Configuration -f $target.Framework -r $target.Runtime `
            --self-contained true -o $packageRoot
        if ($LASTEXITCODE -ne 0) { throw "$packageName publishing failed." }

        # PDB files are useful for diagnostics, but are not required to run the application.
        Get-ChildItem -LiteralPath $packageRoot -Filter '*.pdb' -File |
            Remove-Item -Force

        Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath `
            -CompressionLevel Optimal
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Host "MetricBot release packages created: $releaseRoot"
