param([string]$NinjaUserDir=(Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8'))
$ErrorActionPreference='Stop'
if(Get-Process NinjaTrader -ErrorAction SilentlyContinue){throw 'Close NinjaTrader normally first. Do not stop positions or kill the process via this script.'}
$taskRoot=$PSScriptRoot
$taskBuild=Join-Path $taskRoot '.local\build'
$taskCustom=[IO.Path]::GetFullPath((Join-Path $NinjaUserDir 'bin\Custom'))
$taskReceipt=Get-Content -LiteralPath (Join-Path $taskBuild 'receipt.json') -Raw | ConvertFrom-Json
if($taskReceipt.valid -ne $true){throw 'No completed successful build receipt'}
if($taskReceipt.customDir -ne $taskCustom){throw 'Build belongs to a different NinjaTrader user directory'}
if(!$taskReceipt.inputHashes){throw 'Build receipt lacks installed-input hashes; rebuild'}
foreach($taskInput in $taskReceipt.inputHashes.PSObject.Properties){if(!(Test-Path -LiteralPath $taskInput.Name) -or (Get-FileHash -LiteralPath $taskInput.Name).Hash -ne $taskInput.Value){throw 'Installed DLL/project/source changed after build; rebuild to preserve other customizations'}}
foreach($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src\ninjatrader') -Filter '*.cs'){
 if($taskReceipt.sourceHashes.($taskFile.Name) -ne (Get-FileHash -LiteralPath $taskFile.FullName).Hash){throw 'Sources changed after build; rebuild before installing'}
}
$taskDll=Join-Path $taskBuild 'NinjaTrader.Custom.dll'
if((Get-FileHash -LiteralPath $taskDll).Hash -ne $taskReceipt.nativeHash){throw 'Build hash mismatch'}
$taskBackup=Join-Path (Join-Path $taskRoot '.local\backups') ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
New-Item -ItemType Directory -Force -Path $taskBackup | Out-Null
foreach($taskRelative in @('NinjaTrader.Custom.dll','NinjaTrader.Custom.csproj','Indicators\MNQProtectionPanel.cs','Indicators\ProtectionMath.cs','AddOns\Sim101ApiBridge.cs')){
 $taskExisting=Join-Path $taskCustom $taskRelative
 if(Test-Path -LiteralPath $taskExisting){$taskSaved=Join-Path $taskBackup $taskRelative;New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskSaved) | Out-Null;Copy-Item -LiteralPath $taskExisting -Destination $taskSaved}
}
$taskProject=Join-Path $taskCustom 'NinjaTrader.Custom.csproj'
$taskXml=[xml]::new();$taskXml.Load($taskProject)
if($taskXml.DocumentElement.NamespaceURI){throw 'Unsupported csproj namespace; inspect before adapting installer'}
$taskGroup=$taskXml.SelectSingleNode('/Project/ItemGroup[Compile]')
if(!$taskGroup){throw 'NinjaTrader compile item group missing'}
foreach($taskRelative in @('Indicators\MNQProtectionPanel.cs','Indicators\ProtectionMath.cs','AddOns\Sim101ApiBridge.cs')){
 $taskTarget=Join-Path $taskCustom $taskRelative
 New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskTarget) | Out-Null
 Copy-Item -LiteralPath (Join-Path (Join-Path $taskRoot 'src\ninjatrader') (Split-Path -Leaf $taskRelative)) -Destination $taskTarget
 if(!$taskXml.SelectSingleNode("/Project/ItemGroup/Compile[@Include='$taskRelative']")){$taskNode=$taskXml.CreateElement('Compile');$taskNode.SetAttribute('Include',$taskRelative);[void]$taskGroup.AppendChild($taskNode)}
}
$taskXml.Save($taskProject)
Copy-Item -LiteralPath $taskDll -Destination (Join-Path $taskCustom 'NinjaTrader.Custom.dll')
Write-Output "Installed. Backups: $taskBackup"
Write-Output 'No workspace, account, credential, connection, ATI or firewall settings were changed. Configure Sim101 and the indicator explicitly after login.'
