$ErrorActionPreference='Stop'
$moduleRoot=Split-Path -Parent $PSScriptRoot
$out=Join-Path $moduleRoot '.local\tests'
New-Item -ItemType Directory -Force $out | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe ('/out:'+(Join-Path $out 'RiskTests.exe')) (Join-Path $moduleRoot 'src\ninjatrader\ProtectionMath.cs') (Join-Path $moduleRoot 'tools\RiskTests.cs')
if($LASTEXITCODE){throw 'Test build failed'}
& (Join-Path $out 'RiskTests.exe')
if($LASTEXITCODE){throw 'Risk/lifecycle tests failed'}
