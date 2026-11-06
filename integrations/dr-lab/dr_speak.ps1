param([Parameter(Mandatory=$true)][ValidateSet('NQ','ES','YM')][string]$Instrument)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$drSpeaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $drFemale = @($drSpeaker.GetInstalledVoices() | Where-Object {
        $_.Enabled -and $_.VoiceInfo.Gender -eq 'Female' -and $_.VoiceInfo.Culture.Name -eq 'en-US'
    } | Sort-Object { if ($_.VoiceInfo.Name -eq 'Microsoft Zira Desktop') { 0 } else { 1 } })
    if (-not $drFemale.Count) { throw 'No enabled English female voice installed' }
    $drSpeaker.SelectVoice($drFemale[0].VoiceInfo.Name)
    $drSpeaker.SetOutputToDefaultAudioDevice()
    $drSpeaker.Rate = -1
    $drSpeaker.Volume = 100
    $drLetters = ($Instrument.ToCharArray() | ForEach-Object { [string]$_ }) -join ' '
    $drSpeaker.Speak("Activation D R, $drLetters")
} finally { $drSpeaker.Dispose() }
