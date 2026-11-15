$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskRoot 'suite/dist/foxmolika_CubeMX_Setup_0.4.0_win-x64.exe'
$taskAssembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($taskExe))
$taskHashes = @{}
foreach ($taskName in $taskAssembly.GetManifestResourceNames()) {
    $taskStream = $taskAssembly.GetManifestResourceStream($taskName)
    $taskSha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $taskHashes[$taskName] = [BitConverter]::ToString($taskSha.ComputeHash($taskStream)).Replace('-', '').ToLowerInvariant()
    } finally {
        $taskStream.Dispose()
        $taskSha.Dispose()
    }
}
$taskHashes | ConvertTo-Json
