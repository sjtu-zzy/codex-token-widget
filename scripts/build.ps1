$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe was not found. Install .NET Framework build tools or use a Windows installation with .NET Framework.'
}
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
& $csc /nologo /target:winexe /platform:x64 /optimize+ /win32icon:(Join-Path $root 'assets\CodexTokenWidget.ico') /out:(Join-Path $root 'dist\CodexTokenWidgetPortable.exe') /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $root 'src\CodexTokenWidgetPortable.cs')
Get-Item (Join-Path $root 'dist\CodexTokenWidgetPortable.exe')
