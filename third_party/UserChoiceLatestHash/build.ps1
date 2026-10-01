$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe was not found.' }
$installation = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual C++ x64 tools were not found.' }
$vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
$source = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$output = Join-Path $source 'TzipLatestHash.dll'
$command = 'call "' + $vcvars + '" >nul && cl /nologo /O2 /MT /EHsc /W4 /LD /Fe:"' + $output + '" "' + (Join-Path $source 'TzipHashExport.cpp') + '" "' + (Join-Path $source 'HashTables.cpp') + '" "' + (Join-Path $source 'HashCodec.cpp') + '" /link advapi32.lib crypt32.lib'
Push-Location $source
try {
    & cmd.exe /c $command
    if ($LASTEXITCODE -ne 0) { throw "Native hash build failed: $LASTEXITCODE" }
}
finally { Pop-Location }
