param([Parameter(Mandatory=$true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not $taskExe.StartsWith($taskRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Only audit a workspace artifact.' }
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
