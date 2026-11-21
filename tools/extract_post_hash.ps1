<#
.SYNOPSIS
  从 foxmolika 安装包中提取内嵌的 PostGenerate.exe SHA-256。
.DESCRIPTION
  只读取文件、不运行安装器。用于发布前把上一版官方助手摘要加入
  tools/legacy_post_hashes.json，避免再次出现 0.8.5→0.8.6 的白名单遗漏。
#>
param([Parameter(Mandatory = $true)][string]$Setup)

$ErrorActionPreference = 'Stop'
$tmp = Join-Path $env:TEMP ("foxmolika_post_hash_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function Read-Resource([Reflection.Assembly]$Assembly, [string]$Name) {
    $stream = $Assembly.GetManifestResourceStream($Name)
    if ($null -eq $stream) { throw "missing resource: $Name" }
    $memory = New-Object IO.MemoryStream
    $stream.CopyTo($memory)
    $stream.Close()
    return $memory.ToArray()
}

$copy = Join-Path $tmp 'setup.exe'
Copy-Item -LiteralPath $Setup -Destination $copy -Force
Unblock-File -LiteralPath $copy
$outer = [Reflection.Assembly]::LoadFile($copy)
$names = $outer.GetManifestResourceNames()
$innerName = $names | Where-Object { $_ -eq 'payload/foxmolika_ProjectSetup.exe' } | Select-Object -First 1
if (-not $innerName) {
    $innerName = $names | Where-Object { $_ -eq 'payload/foxmolika_MapEditor.exe' } | Select-Object -First 1
}
if (-not $innerName) { throw "no nested ProjectSetup/MapEditor in $Setup" }
$innerPath = Join-Path $tmp 'inner.exe'
[IO.File]::WriteAllBytes($innerPath, (Read-Resource $outer $innerName))
Unblock-File -LiteralPath $innerPath
$inner = [Reflection.Assembly]::LoadFile($innerPath)
$payload = Read-Resource $inner 'payload/foxmolika_PostGenerate.exe'
$sha = [Security.Cryptography.SHA256]::Create()
$digest = (($sha.ComputeHash($payload) | ForEach-Object { $_.ToString('x2') }) -join '')
Write-Output $digest
