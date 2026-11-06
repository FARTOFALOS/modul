param()
$ErrorActionPreference='Stop'
function Invoke-SimRaw([hashtable]$Body){
 $taskPipe=[IO.Pipes.NamedPipeClientStream]::new('.','MNQ-Sim101-Api-v1',[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
 try{
  $taskPipe.Connect(3000)
  $taskWriter=[IO.StreamWriter]::new($taskPipe,[Text.UTF8Encoding]::new($false),4096,$true);$taskWriter.AutoFlush=$true
  $taskWriter.WriteLine(($Body|ConvertTo-Json -Compress -Depth 10))
  $taskReader=[IO.StreamReader]::new($taskPipe,[Text.Encoding]::UTF8,$false,4096,$true);$taskRead=$taskReader.ReadLineAsync()
  if(!$taskRead.Wait(7000)){throw 'API response timed out'}
  $taskRead.Result|ConvertFrom-Json
 }finally{$taskPipe.Dispose()}
}
$taskStatus=Invoke-SimRaw @{op='status';account='Sim101'}
$taskSession=$taskStatus.session
$taskTests=@()
$taskPositionBefore=$taskStatus.position|ConvertTo-Json -Compress
$taskOrdersBefore=@($taskStatus.orders|ForEach-Object { $_.id+"|"+$_.state+"|"+$_.quantity+"|"+$_.stop }) -join ";"
$taskDenied=Invoke-SimRaw @{op='place_entry';account='OneUp';instrument='MNQ DEC26';session=$taskSession;requestId='deny-real-0001';side='BUY';quantity=1;confirm='SIM101_ONLY'}
if($taskDenied.ok -or $taskDenied.message -notlike 'ACCOUNT_DENIED*'){throw 'Live account guard failed'}
$taskTests+='server rejects OneUp before order logic'
$taskDenied=Invoke-SimRaw @{op='place_entry';account='Sim101';instrument='NQ DEC26';session=$taskSession;requestId='deny-symbol-0001';side='BUY';quantity=1;confirm='SIM101_ONLY'}
if($taskDenied.ok -or $taskDenied.message -notlike 'INSTRUMENT_DENIED*'){throw 'Instrument guard failed'}
$taskTests+='server rejects other symbols'
$taskDenied=Invoke-SimRaw @{op='place_entry';account='Sim101';instrument='MNQ DEC26';session='old-session';requestId='deny-stale-0001';side='BUY';quantity=1;confirm='SIM101_ONLY'}
if($taskDenied.ok -or $taskDenied.message -notlike 'SESSION_REQUIRED*'){throw 'Session guard failed'}
$taskTests+='server rejects stale session'
$taskDenied=Invoke-SimRaw @{op='place_entry';account='Sim101';instrument='MNQ DEC26';session=$taskSession;requestId='deny-qty-0001';side='BUY';quantity=2;confirm='SIM101_ONLY'}
if($taskDenied.ok -or $taskDenied.message -notlike 'QUANTITY_DENIED*'){throw 'Quantity guard failed'}
$taskTests+='server rejects quantity2'
$taskDenied=Invoke-SimRaw @{op='flatten_all';account='Sim101'}
if($taskDenied.ok -or $taskDenied.message -ne 'OPERATION_DENIED'){throw 'Operation guard failed'}
$taskTests+='server rejects broad flatten command'
$taskClose=Invoke-SimRaw @{op='close_test';account='Sim101';session=$taskSession;requestId='deny-unowned-close-0001'}
if($taskClose.ok -or $taskClose.message -notlike 'No filled API*'){throw 'Close ownership guard failed'}
$taskTests+='server rejects close without owned entry'
$taskPreviewId='preview-'+[guid]::NewGuid().ToString('N')
$taskPreview=@{op='preview_entry';account='Sim101';session=$taskSession;requestId=$taskPreviewId;side='BUY';quantity=1}
$taskFirst=Invoke-SimRaw $taskPreview
$taskRepeat=Invoke-SimRaw $taskPreview
if(($taskFirst|ConvertTo-Json -Depth 10 -Compress) -ne ($taskRepeat|ConvertTo-Json -Depth 10 -Compress)){throw 'Idempotent replay differed'}
$taskPreview.side='SELL'
$taskConflict=Invoke-SimRaw $taskPreview
if($taskConflict.ok -or $taskConflict.message -ne 'REQUEST_ID_CONFLICT'){throw 'Request-id conflict guard failed'}
$taskTests+='preview replay returns the original response; conflicting reuse is rejected'
$taskAfter=Invoke-SimRaw @{op='status';account='Sim101'}
if(($taskAfter.position|ConvertTo-Json -Compress) -ne $taskPositionBefore -or ((@($taskAfter.orders|ForEach-Object { $_.id+"|"+$_.state+"|"+$_.quantity+"|"+$_.stop }) -join ";") -ne $taskOrdersBefore)){throw 'Simulation position/order state changed during probe; inspect before proceeding'}
[pscustomobject]@{checks=$taskTests;status=$taskAfter}|ConvertTo-Json -Depth 10
