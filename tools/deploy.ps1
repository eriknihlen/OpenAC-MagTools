#requires -Version 7.0
<#
.SYNOPSIS
    Builds the plugin and installs it into the client's plugin directory.

.DESCRIPTION
    The host discovers plugins in the immediate subdirectories of its plugins
    root and requires a plugin.json in each, so this drops the assembly, its
    .deps.json, the markup files and the manifest into one directory named
    after the plugin. This mirrors what the client's own build does for the
    plugin that ships in-tree.

    The plugins root is <data directory>/plugins:
      Windows  %LOCALAPPDATA%\acdream\plugins
      macOS    ~/Library/Application Support/acdream/plugins
      Linux    $XDG_DATA_HOME/acdream/plugins (default ~/.local/share/acdream/plugins)
    ACDREAM_DATA_DIR overrides the data directory, and -PluginsRoot overrides
    everything.

.PARAMETER Configuration
    Build configuration. Release by default.

.PARAMETER OpenAcRoot
    The OpenAC checkout to compile against. Defaults to the repository's own
    default (the sibling acdream/OpenAC).

.PARAMETER PluginsRoot
    Install into this plugins root instead of the resolved default.

.PARAMETER NoBuild
    Install whatever is already in bin/<Configuration>/ without rebuilding.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OpenAcRoot,
    [string] $PluginsRoot,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src/OpenAC.MagTools/OpenAC.MagTools.csproj'
$pluginDirectoryName = 'OpenAC.MagTools'

function Resolve-PluginsRoot {
    if ($PluginsRoot) { return $PluginsRoot }

    if ($env:ACDREAM_DATA_DIR) {
        return (Join-Path $env:ACDREAM_DATA_DIR 'plugins')
    }

    if ($IsWindows) {
        return (Join-Path $env:LOCALAPPDATA 'acdream/plugins')
    }
    if ($IsMacOS) {
        return (Join-Path $HOME 'Library/Application Support/acdream/plugins')
    }

    $dataHome = if ($env:XDG_DATA_HOME) { $env:XDG_DATA_HOME } else { Join-Path $HOME '.local/share' }
    return (Join-Path $dataHome 'acdream/plugins')
}

if (-not $NoBuild) {
    $buildArguments = @('build', $projectPath, '-c', $Configuration)
    if ($OpenAcRoot) {
        $buildArguments += "-p:OpenAcRoot=$OpenAcRoot"
    }

    Write-Host "Building $pluginDirectoryName ($Configuration)..."
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

$buildOutput = Join-Path $repositoryRoot "src/OpenAC.MagTools/bin/$Configuration/net10.0"
if (-not (Test-Path $buildOutput)) {
    throw "No build output at $buildOutput. Build first, or drop -NoBuild."
}

$destination = Join-Path (Resolve-PluginsRoot) $pluginDirectoryName
New-Item -ItemType Directory -Force -Path $destination | Out-Null

# The assembly, its dependency manifest, the markup and the plugin manifest.
# The abstractions assembly is deliberately NOT copied: the host supplies it,
# and a second copy would give every interface two identities.
$files = @(
    'OpenAC.MagTools.dll',
    'OpenAC.MagTools.deps.json',
    'magtools.xml',
    'magtools-hud.xml',
    'plugin.json'
)

foreach ($file in $files) {
    $source = Join-Path $buildOutput $file
    if (-not (Test-Path $source)) {
        throw "Expected build output $file is missing from $buildOutput."
    }
    Copy-Item -Path $source -Destination (Join-Path $destination $file) -Force
}

$pdb = Join-Path $buildOutput 'OpenAC.MagTools.pdb'
if (Test-Path $pdb) {
    Copy-Item -Path $pdb -Destination (Join-Path $destination 'OpenAC.MagTools.pdb') -Force
}

Write-Host "Installed to $destination"
Get-ChildItem $destination | Select-Object -ExpandProperty Name
