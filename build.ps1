$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 Developer Pack, then retry on Windows.' }
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
& $compiler /nologo /target:winexe /optimize+ "/out:$PSScriptRoot\ZipFilenameSearch.exe" @references /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\SearchEngine.cs" "$PSScriptRoot\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Write-Host "Built $PSScriptRoot\ZipFilenameSearch.exe"
