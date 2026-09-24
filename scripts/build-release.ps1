[CmdletBinding()]
param(
    [string]$Version = "",
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\App\DesktopIconsStorage.App.csproj"
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\$Runtime"))
$releaseDir = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "release"))
$installerOutputDir = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "installer"))
$installerScript = Join-Path $repoRoot "build\installer\DesktopIconsStorage.iss"

# 功能分支仅做 Debug/烟测；安装包与便携版只能从 main 或已合并到 main 的标签构建。
$branchOutput = & git -C $repoRoot branch --show-current
if ($LASTEXITCODE -ne 0) { throw "无法确认发布来源分支。" }
$currentBranch = if ($null -eq $branchOutput) { "" } else { [string]$branchOutput }
$currentBranch = $currentBranch.Trim()
if ($currentBranch -ne "main") {
    if ($currentBranch -ne "") {
        throw "仅允许在 main 分支构建发布包；当前分支：$currentBranch"
    }
    & git -C $repoRoot merge-base --is-ancestor HEAD origin/main
    if ($LASTEXITCODE -ne 0) {
        throw "当前标签提交尚未合并到 main，不能构建发布包。"
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
    $versionNode = $projectXml.Project.PropertyGroup |
        Where-Object { $null -ne $_.Version } |
        Select-Object -First 1
    $Version = [string]$versionNode.Version
}

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "版本号格式无效：$Version"
}

function Assert-ArtifactPath([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作 artifacts 目录之外的路径：$fullPath"
    }
}

foreach ($directory in @($publishDir, $releaseDir, $installerOutputDir)) {
    Assert-ArtifactPath $directory
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

Write-Host "发布 DesktopIconsStorage $Version ($Runtime)..."
& dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

$publishedExe = Join-Path $publishDir "DesktopIconsStorage.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "发布结果缺少 DesktopIconsStorage.exe"
}

$portableExe = Join-Path $releaseDir "DesktopIconsStorage-$Version-$Runtime.exe"
Copy-Item -LiteralPath $publishedExe -Destination $portableExe -Force
Write-Host "便携版：$portableExe"

function Write-Checksums {
    $checksumFile = Join-Path $releaseDir "SHA256SUMS.txt"
    $targets = @(
        Get-ChildItem -LiteralPath $releaseDir -Filter "*.exe" -File -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $installerOutputDir -Filter "*.exe" -File -ErrorAction SilentlyContinue
    )
    $lines = $targets | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
    Set-Content -LiteralPath $checksumFile -Value $lines -Encoding ascii
    Write-Host "校验文件：$checksumFile"
}

if ($SkipInstaller) {
    Write-Checksums
    return
}

$isccCandidates = @(
    (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) "Inno Setup 6\ISCC.exe"),
    (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "Inno Setup 6\ISCC.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if ($null -eq $isccCandidates) {
    throw "未找到 Inno Setup 6。可使用 -SkipInstaller 仅生成便携版。"
}

Write-Host "生成安装包..."
& $isccCandidates "/DAppVersion=$Version" "/DRuntime=$Runtime" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出码：$LASTEXITCODE"
}

$installerExe = Join-Path $installerOutputDir "DesktopIconsStorage-Setup-$Version-$Runtime.exe"
if (-not (Test-Path -LiteralPath $installerExe)) {
    throw "安装包未生成：$installerExe"
}
Write-Host "安装包：$installerExe"
Write-Checksums
