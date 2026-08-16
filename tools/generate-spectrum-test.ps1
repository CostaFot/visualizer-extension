<#
.SYNOPSIS
    Generates (and optionally plays) tools/spectrum-test.wav — the visualizer's test signal.

.DESCRIPTION
    The signal has two parts, designed against the band layout in Audio/SpectrumCapture.cs
    (log-spaced bands, 40 Hz–16 kHz — keep BandCount here in sync with BarCount in
    Pages/VisualizerDockBand.cs):

      1. One 1.2 s sine tone at the geometric center of each band, in order — each tone should
         light primarily ONE bar, marching left to right.
      2. An 8 s logarithmic sweep 40 Hz -> 16 kHz — a single peak gliding across all bars.

    20 ms fades on every segment avoid clicks. Output: 16-bit mono WAV at 44.1 kHz.

.PARAMETER Play
    Also play the file synchronously after writing it.

.EXAMPLE
    .\generate-spectrum-test.ps1 -Play
#>
param(
    [switch]$Play,
    [int]$BandCount = 8,
    [double]$MinFrequency = 40,
    [double]$MaxFrequency = 16000,
    [double]$ToneSeconds = 1.2,
    [double]$SweepSeconds = 8,
    [double]$Amplitude = 0.35,
    [string]$OutFile = "$PSScriptRoot\spectrum-test.wav"
)

$sr = 44100
$centers = 0..($BandCount - 1) | ForEach-Object {
    $MinFrequency * [math]::Pow($MaxFrequency / $MinFrequency, ($_ + 0.5) / $BandCount)
}
Write-Host ("Band tones (Hz): " + (($centers | ForEach-Object { [math]::Round($_) }) -join ', '))

$samples = New-Object System.Collections.Generic.List[float]
foreach ($f in $centers) {
    $n = [int]($sr * $ToneSeconds)
    for ($i = 0; $i -lt $n; $i++) {
        $env = [math]::Min(1.0, [math]::Min($i / ($sr * 0.02), ($n - $i) / ($sr * 0.02)))
        $samples.Add([float]($Amplitude * $env * [math]::Sin(2 * [math]::PI * $f * $i / $sr)))
    }
}

# Log sweep (phase-accumulated so the frequency glide is continuous)
$n = [int]($sr * $SweepSeconds)
$k = [math]::Log($MaxFrequency / $MinFrequency)
$phase = 0.0
for ($i = 0; $i -lt $n; $i++) {
    $f = $MinFrequency * [math]::Exp($k * $i / $n)
    $phase += 2 * [math]::PI * $f / $sr
    $env = [math]::Min(1.0, [math]::Min($i / ($sr * 0.02), ($n - $i) / ($sr * 0.02)))
    $samples.Add([float]($Amplitude * $env * [math]::Sin($phase)))
}

# 16-bit mono WAV
$dataLen = $samples.Count * 2
$stream = [IO.File]::Create($OutFile)
$bw = New-Object IO.BinaryWriter($stream)
$bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $dataLen))
$bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $bw.Write([int]16)
$bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$sr); $bw.Write([int]($sr * 2))
$bw.Write([int16]2); $bw.Write([int16]16)
$bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]$dataLen)
foreach ($s in $samples) { $bw.Write([int16]([math]::Round($s * 32767))) }
$bw.Dispose()

Write-Host "Written: $OutFile ($([math]::Round($samples.Count / $sr, 1)) s)"
if ($Play) {
    Write-Host "Playing — watch the dock band."
    (New-Object System.Media.SoundPlayer($OutFile)).PlaySync()
    Write-Host "Done."
}
