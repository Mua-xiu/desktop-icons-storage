[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$sandboxRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "test-sandbox"))

# 测试目录必须固定在被 Git 忽略的 artifacts 中，不能指向真实桌面。
if (-not $sandboxRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "测试目录不在 artifacts 内：$sandboxRoot"
}

$testDesktop = Join-Path $sandboxRoot "Desktop"
$testStorage = Join-Path $sandboxRoot "Storage"
$testConfig = Join-Path $sandboxRoot "Config"
$buildOutput = Join-Path $sandboxRoot "Build"
foreach ($path in @($testDesktop, $testStorage, $testConfig)) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

# 只复制仓库自带应用图标，并创建普通测试文件；不读取真实桌面图标文件。
Copy-Item -LiteralPath (Join-Path $repoRoot "src\App\Assets\app.ico") `
    -Destination (Join-Path $testDesktop "应用图标.ico") -Force
$sampleNote = Join-Path $testDesktop "测试说明.txt"
if (-not (Test-Path -LiteralPath $sampleNote)) {
    [IO.File]::WriteAllText($sampleNote, "此文件只用于收纳筐隔离测试。", [Text.UTF8Encoding]::new($false))
}
$sampleFolder = Join-Path $testDesktop "测试文件夹"
New-Item -ItemType Directory -Path $sampleFolder -Force | Out-Null

# 首次运行预置一个空链接筐，便于验证小盒与居中弹窗；之后保留测试布局。
$layoutPath = Join-Path $testConfig "layout.json"
$basketFolder = Join-Path $testStorage "测试收纳筐"
New-Item -ItemType Directory -Path $basketFolder -Force | Out-Null
if (-not (Test-Path -LiteralPath $layoutPath)) {
    $basket = [ordered]@{
        id = [Guid]::NewGuid().ToString()
        name = "测试收纳筐"
        mode = "link"
        previewRows = 2
        previewColumns = 2
        folderPath = $basketFolder
        x = 100
        y = 100
        width = 152
        height = 159
        collapsed = $false
        itemOrder = @()
    }
    [IO.File]::WriteAllText($layoutPath,
        (ConvertTo-Json -InputObject @($basket) -Depth 5),
        [Text.UTF8Encoding]::new($false))
}

# 示例快捷方式只指向上面生成的沙盒文件与应用图标副本。
$shell = New-Object -ComObject WScript.Shell
foreach ($sample in @(
    @{ Name = "应用图标.lnk"; Target = (Join-Path $testDesktop "应用图标.ico") },
    @{ Name = "测试说明.lnk"; Target = $sampleNote },
    @{ Name = "测试文件夹.lnk"; Target = $sampleFolder }
)) {
    $linkPath = Join-Path $basketFolder $sample.Name
    if (-not (Test-Path -LiteralPath $linkPath)) {
        $shortcut = $shell.CreateShortcut($linkPath)
        $shortcut.TargetPath = $sample.Target
        $shortcut.Save()
    }
}

$previousRoot = $env:DESKTOPICONSSTORAGE_TEST_ROOT
try {
    $env:DESKTOPICONSSTORAGE_TEST_ROOT = $sandboxRoot
    $project = Join-Path $repoRoot "src\App\DesktopIconsStorage.App.csproj"
    $application = Join-Path $buildOutput "DesktopIconsStorage.exe"
    Write-Host "测试桌面：$testDesktop"
    Write-Host "测试收纳目录：$testStorage"
    if (-not $NoBuild) {
        & dotnet build $project --configuration Debug "-p:OutputPath=$buildOutput" -v:q
        if ($LASTEXITCODE -ne 0) { throw "测试构建失败：$LASTEXITCODE" }
    }
    if (-not (Test-Path -LiteralPath $application)) {
        throw "缺少测试程序：$application"
    }
    & $application
    # WinExe 启动后 PowerShell 可能不设置 LASTEXITCODE；进程仍会独立运行。
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "测试程序退出码：$LASTEXITCODE"
    }
}
finally {
    $env:DESKTOPICONSSTORAGE_TEST_ROOT = $previousRoot
}
