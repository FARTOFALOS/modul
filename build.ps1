param(
 [string]$NinjaUserDir=(Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8'),
 [string]$NinjaInstallBin='C:\Program Files\NinjaTrader 8\bin'
)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskOut=Join-Path $taskRoot '.local\build'
$taskFramework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCsc=Join-Path $taskFramework 'csc.exe'
$taskCustom=Join-Path $NinjaUserDir 'bin\Custom'
foreach($taskPath in @($taskCsc,(Join-Path $taskCustom 'NinjaTrader.Custom.csproj'),(Join-Path $taskCustom 'NinjaTrader.Custom.dll'),(Join-Path $NinjaInstallBin 'Microsoft.CodeAnalysis.CSharp.dll'))){if(!(Test-Path -LiteralPath $taskPath)){throw "Missing dependency: $taskPath"}}
New-Item -ItemType Directory -Force -Path $taskOut | Out-Null
'{"valid":false}' | Set-Content -LiteralPath (Join-Path $taskOut 'receipt.json') -Encoding UTF8
$taskInputPaths=@((Join-Path $taskCustom 'NinjaTrader.Custom.csproj'),(Join-Path $taskCustom 'NinjaTrader.Custom.dll'))
$taskProjectXml=[xml]::new();$taskProjectXml.Load((Join-Path $taskCustom 'NinjaTrader.Custom.csproj'))
foreach($taskItem in $taskProjectXml.SelectNodes('/Project/ItemGroup/Compile')){
 $taskInputPaths+=Join-Path $taskCustom ([Uri]::UnescapeDataString($taskItem.Include))
}
$taskInputs=@{}
foreach($taskPath in $taskInputPaths|Select-Object -Unique){$taskInputs[[IO.Path]::GetFullPath($taskPath)]=(Get-FileHash -LiteralPath $taskPath).Hash}
& $taskCsc /nologo /target:exe ('/out:'+(Join-Path $taskOut 'RiskTests.exe')) (Join-Path $taskRoot 'src\ninjatrader\ProtectionMath.cs') (Join-Path $taskRoot 'tools\RiskTests.cs')
if($LASTEXITCODE){throw 'Risk test compilation failed'}
& (Join-Path $taskOut 'RiskTests.exe')
if($LASTEXITCODE){throw 'Risk tests failed'}
& $taskCsc /nologo /reference:System.Core.dll /reference:System.Web.Extensions.dll ('/out:'+(Join-Path $taskOut 'SimApiClient.exe')) (Join-Path $taskRoot 'tools\SimApiClient.cs')
if($LASTEXITCODE){throw 'Client compilation failed'}
$taskRefs=@('/reference:System.Core.dll','/reference:System.Xml.Linq.dll',('/reference:'+(Join-Path $taskFramework 'netstandard.dll')))
foreach($taskName in @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','System.Collections.Immutable.dll','System.Memory.dll')){$taskRefs+=('/reference:'+(Join-Path $NinjaInstallBin $taskName))}
& $taskCsc /nologo @taskRefs ('/out:'+(Join-Path $taskOut 'BuildNative.exe')) (Join-Path $taskRoot 'tools\BuildNative.cs')
if($LASTEXITCODE){throw 'Native builder compilation failed'}
& (Join-Path $taskOut 'BuildNative.exe') $taskCustom (Join-Path $taskOut 'NinjaTrader.Custom.dll') $NinjaInstallBin (Join-Path $taskRoot 'src\ninjatrader')
if($LASTEXITCODE){throw 'Full native compilation failed'}
$taskHashes=@{}
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src\ninjatrader') -Filter '*.cs' | ForEach-Object {$taskHashes[$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
foreach($taskPath in $taskInputs.Keys){if((Get-FileHash -LiteralPath $taskPath).Hash -ne $taskInputs[$taskPath]){throw 'Installed inputs changed during build; no valid receipt produced'}}
@{valid=$true;sourceHashes=$taskHashes;inputHashes=$taskInputs;nativeHash=(Get-FileHash -LiteralPath (Join-Path $taskOut 'NinjaTrader.Custom.dll')).Hash;customDir=[IO.Path]::GetFullPath($taskCustom);builtUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskOut 'receipt.json') -Encoding UTF8
Write-Output "Build complete; no installed files changed. Output: $taskOut"
