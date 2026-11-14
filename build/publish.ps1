<#
.SYNOPSIS
  Build LunaDNS bản phát hành (KH7). Nguồn duy nhất cho máy PO, GitHub Actions và Chay-tu-ma-nguon.cmd.
.OUTPUTS
  artifacts/publish/win-x64/          thư mục chạy được (exe + DLL native)
  artifacts/LunaDNS-<v>-portable.zip  trừ khi -SkipZip
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipZip,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/LunaDns.App/LunaDns.App.csproj'
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish/win-x64'

if (-not $Version) {
    $Version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version phải dạng x.y.z, nhận được '$Version'." }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# DebugType truyền toàn cục: trong pubxml nó chỉ áp cho App, Core/Platform vẫn ra .pdb rời.
dotnet publish $project -p:PublishProfile=win-x64 -p:Version=$Version -p:DebugType=embedded -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish lỗi ($LASTEXITCODE)." }

# Kiểm hình dạng bản build: exe single-file, không còn DLL managed của LunaDNS, không .pdb (DebugType=embedded).
$exe = Join-Path $publishDir 'LunaDNS.exe'
if (-not (Test-Path $exe)) { throw 'Thiếu LunaDNS.exe.' }
$stray = Get-ChildItem $publishDir -File | Where-Object { $_.Name -like 'LunaDns.*.dll' -or $_.Extension -eq '.pdb' }
if ($stray) { throw "Bản build không phải single-file: $($stray.Name -join ', ')" }
Get-ChildItem $publishDir -File | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }

if (-not $SkipZip) {
    $zip = Join-Path $artifacts "LunaDNS-$Version-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $staging = Join-Path $artifacts "LunaDNS-$Version"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Copy-Item $publishDir $staging -Recurse
    # Hướng dẫn đi kèm zip; CRLF + BOM để Notepad cũ hiển thị đúng tiếng Việt.
    $guide = (Get-Content (Join-Path $PSScriptRoot 'HUONG-DAN.txt') -Raw -Encoding utf8) -replace '\{VERSION\}', $Version
    $guide = $guide -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText((Join-Path $staging 'HUONG-DAN.txt'), $guide, [Text.UTF8Encoding]::new($true))
    Compress-Archive -Path $staging -DestinationPath $zip
    Remove-Item $staging -Recurse -Force
    '{0} {1:N1} MB' -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)
}

if (-not $SkipInstaller) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($cmd) { $cmd.Source } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path $iscc)) { throw 'Không tìm thấy ISCC.exe (Inno Setup 6). Cài: winget install --id JRSoftware.InnoSetup -e' }
    & $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/O$artifacts" (Join-Path $PSScriptRoot 'installer/LunaDNS.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC lỗi ($LASTEXITCODE)." }
}

$outputs = Get-ChildItem $artifacts -File | Where-Object { $_.Name -like "LunaDNS-*$Version*" -and $_.Extension -in '.exe', '.zip' }
if ($outputs) {
    $outputs | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
        Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Get-Content (Join-Path $artifacts 'SHA256SUMS.txt')
}
