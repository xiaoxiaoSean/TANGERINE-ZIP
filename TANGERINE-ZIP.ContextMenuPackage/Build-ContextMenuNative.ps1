param([string] $OutputDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\context-menu-native'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$vsWherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vsWherePath)) {
    throw 'CTXNB0001: Visual Studio Installer was not found.' #CTXNB0001
}
$visualStudioDirectory = (& $vsWherePath -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($visualStudioDirectory)) {
    throw 'CTXNB0002: Visual Studio C++ x64 build tools were not found.' #CTXNB0002
}
$msBuildPath = Join-Path $visualStudioDirectory 'MSBuild\Current\Bin\MSBuild.exe'
$projectPath = Join-Path $repositoryRoot 'third_party\ContextMenuForWindows11\ContextMenuCustomHost\ContextMenuCustomHost.vcxproj'

function Invoke-CleanProcess([string] $FilePath, [string[]] $Arguments) {
    $temporaryPath = Join-Path $repositoryRoot 'artifacts\msbuild-temp'
    New-Item -ItemType Directory -Path $temporaryPath -Force | Out-Null
    $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment.Clear()
    foreach ($name in @('SystemRoot', 'SystemDrive', 'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH', 'LOCALAPPDATA', 'APPDATA',
        'ProgramFiles', 'ProgramFiles(x86)', 'CommonProgramFiles', 'CommonProgramFiles(x86)', 'ProgramData', 'ALLUSERSPROFILE',
        'PUBLIC', 'ComSpec', 'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $startInfo.Environment[$name] = $value }
    }
    $startInfo.Environment['Path'] = [Environment]::GetEnvironmentVariable('Path')
    $startInfo.Environment['TEMP'] = $temporaryPath
    $startInfo.Environment['TMP'] = $temporaryPath
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $process.Dispose()
    return $exitCode
}

$packagesRoot = Join-Path (Split-Path -Parent $projectPath) 'packages'
$cppWinRtProps = Join-Path $packagesRoot 'Microsoft.Windows.CppWinRT.2.0.240405.15\build\native\Microsoft.Windows.CppWinRT.props'
$wilTargets = Join-Path $packagesRoot 'Microsoft.Windows.ImplementationLibrary.1.0.240803.1\build\native\Microsoft.Windows.ImplementationLibrary.targets'
if (-not (Test-Path -LiteralPath $cppWinRtProps) -or -not (Test-Path -LiteralPath $wilTargets)) {
    $restoreExitCode = Invoke-CleanProcess $msBuildPath @($projectPath, '/t:Restore', '/p:RestorePackagesConfig=true', '/p:Configuration=Release', '/p:Platform=x64', '/nologo')
    if ($restoreExitCode -ne 0) { throw 'CTXNB0003: Restoring native dependencies failed.' } #CTXNB0003
}
$buildExitCode = Invoke-CleanProcess $msBuildPath @($projectPath, '/t:Build', '/p:Configuration=Release', '/p:Platform=x64', '/m', '/nologo')
if ($buildExitCode -ne 0) { throw 'CTXNB0004: Building the open-source Explorer command failed.' } #CTXNB0004

$nativeDll = Join-Path $repositoryRoot 'third_party\ContextMenuForWindows11\ContextMenuCustomHost\bin\x64\Release\TangerineZipContextMenuHost.dll'
if (-not (Test-Path -LiteralPath $nativeDll)) { throw 'CTXNB0005: The native output DLL was not produced.' } #CTXNB0005
Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $OutputDirectory 'TangerineZipContextMenuHost.dll') -Force

$compilerVersion = Get-ChildItem -LiteralPath (Join-Path $visualStudioDirectory 'VC\Tools\MSVC') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'bin\Hostx64\x64\cl.exe') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compilerPath = Join-Path $compilerVersion.FullName 'bin\Hostx64\x64\cl.exe'
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $sdkRoot "Lib\$($_.Name)\um\x64\kernel32.lib") } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $compilerVersion -or $null -eq $sdkVersion) { throw 'CTXNB0006: Native compiler or Windows SDK is incomplete.' } #CTXNB0006

$commonArguments = @('/nologo', '/EHsc', '/O2', '/MT', '/DUNICODE', '/D_UNICODE',
    "/I$(Join-Path $compilerVersion.FullName 'include')", "/I$(Join-Path $sdkVersion.FullName 'ucrt')",
    "/I$(Join-Path $sdkVersion.FullName 'shared')", "/I$(Join-Path $sdkVersion.FullName 'um')")
$linkerArguments = @('/MACHINE:X64', '/SUBSYSTEM:WINDOWS',
    "/LIBPATH:$(Join-Path $compilerVersion.FullName 'lib\x64')",
    "/LIBPATH:$(Join-Path $sdkRoot "Lib\$($sdkVersion.Name)\ucrt\x64")",
    "/LIBPATH:$(Join-Path $sdkRoot "Lib\$($sdkVersion.Name)\um\x64")")
& $compilerPath @commonArguments (Join-Path $scriptRoot 'ContextMenuHost.cpp') "/Fo$(Join-Path $OutputDirectory 'ContextMenuHost.obj')" "/Fe$(Join-Path $OutputDirectory 'ContextMenuHost.exe')" /link @linkerArguments kernel32.lib
if ($LASTEXITCODE -ne 0) { throw 'CTXNB0007: Building the package host failed.' } #CTXNB0007
