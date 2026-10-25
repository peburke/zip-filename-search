$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path ([IO.Path]::GetTempPath()) ('ZipSearchTests-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe "/out:$testExe" /r:System.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "$PSScriptRoot\SearchEngine.cs" "$PSScriptRoot\Tests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe } }
