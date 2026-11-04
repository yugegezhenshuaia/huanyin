param(
    [string]$Payload = "",                 # 可选：要内嵌进单文件版的 ffmpeg.exe
    [string]$OutDir = "",
    [switch]$NoLight,                      # 只做单文件版
    [switch]$NoBundle                      # 只做轻量版
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$srcDir = Join-Path $root "src"
if (-not $OutDir) { $OutDir = Join-Path $root "..\dist" }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---------- 找 csc.exe ----------
$csc = $null
foreach ($p in @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)) { if (Test-Path $p) { $csc = $p; break } }
if (-not $csc) { throw "找不到 .NET Framework 4 的 csc.exe，无法编译。" }

Write-Host "compiler : $csc"

# ---------- 图标 ----------
$ico = Join-Path $root "app.ico"
if (-not (Test-Path $ico)) {
    Write-Host "generating icon ..."
    & (Join-Path $root "make-icon.ps1") -Out $ico
}

# ---------- 源文件 ----------
$sources = Get-ChildItem -Path $srcDir -Filter *.cs | Sort-Object Name
Write-Host ("sources  : " + ($sources.Name -join ", "))

$refs = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.IO.Compression.dll",
    "System.IO.Compression.FileSystem.dll",
    "System.Xml.dll"
)

function Invoke-Build {
    param(
        [string]$OutFile,
        [string[]]$ExtraArgs
    )
    $cscArgs = @(
        "/nologo", "/target:winexe", "/platform:anycpu", "/optimize+",
        "/codepage:65001", "/warn:2", "/utf8output",
        "/win32icon:$ico",
        "/win32manifest:$(Join-Path $root 'app.manifest')",
        "/out:$OutFile"
    )
    foreach ($r in $refs) { $cscArgs += "/r:$r" }
    if ($ExtraArgs) { $cscArgs += $ExtraArgs }
    $cscArgs += ($sources | ForEach-Object { $_.FullName })

    Write-Host ""
    Write-Host "building -> $OutFile"
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }
    $size = (Get-Item $OutFile).Length
    Write-Host ("OK  {0}  ({1} KB)" -f (Split-Path $OutFile -Leaf), [math]::Round($size / 1KB, 1))
    return $OutFile
}

$outputs = @()

# ---------- 1) 轻量版 ----------
if (-not $NoLight) {
    $out = Join-Path $OutDir "焕音.exe"
    Invoke-Build -OutFile $out -ExtraArgs @() | Out-Null
    $outputs += $out
}

# ---------- 2) 单文件版（内嵌 ffmpeg） ----------
if (-not $NoBundle) {
    if (-not $Payload -or -not (Test-Path $Payload)) {
        $auto = @(
            (Join-Path $root "..\ffmpeg_dl\tar41\package\ffmpeg.exe"),
            (Join-Path $root "..\ffmpeg_dl\ffmpeg.exe"),
            (Join-Path $root "ffmpeg.exe")
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($auto) { $Payload = [System.IO.Path]::GetFullPath($auto) }
    }
    if ($Payload -and (Test-Path $Payload)) {
        Write-Host "embedding ffmpeg: $Payload ($([math]::Round((Get-Item $Payload).Length/1MB,1)) MB)"
        $gz = Join-Path $root "payload.bin"
        if (Test-Path $gz) { Remove-Item $gz -Force }
        $inStream = [System.IO.File]::OpenRead($Payload)
        $outStream = [System.IO.File]::Create($gz)
        $gzs = New-Object System.IO.Compression.GZipStream($outStream, [System.IO.Compression.CompressionLevel]::Optimal)
        $inStream.CopyTo($gzs, 1MB)
        $gzs.Dispose(); $outStream.Dispose(); $inStream.Dispose()
        Write-Host "payload  : $([math]::Round((Get-Item $gz).Length/1MB,1)) MB (gzip)"

        $out2 = Join-Path $OutDir "焕音-单文件版.exe"
        Invoke-Build -OutFile $out2 -ExtraArgs @("/resource:$gz,FFMPEG_PAYLOAD") | Out-Null
        $outputs += $out2
        Remove-Item $gz -Force
    } else {
        Write-Host "skip 单文件版: 没找到 ffmpeg.exe 作 payload"
    }
}

Write-Host ""
Write-Host "输出目录: $OutDir"
foreach ($o in $outputs) {
    Write-Host ("  " + (Split-Path $o -Leaf) + "  " + [math]::Round((Get-Item $o).Length / 1MB, 2) + " MB")
}
