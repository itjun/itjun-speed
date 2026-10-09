# 用 GitHub CLI 打包并发布「内网测速」的 amd64（win-x64）MSIX。
# 不编译、不上传 ARM。最低版本高于发布版本、或 MSIX 版本与产品版本不一致时直接失败。
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File packaging/publish.ps1 -CheckOnly
#   powershell -NoProfile -ExecutionPolicy Bypass -File packaging/publish.ps1 -Notes "更新说明"

[CmdletBinding()]
param(
    [string]$Version,
    [string]$MinVersion,
    [string]$Notes = "",
    [string]$Repo = "itjun/itjun-speed",
    [switch]$CheckOnly,
    [switch]$Draft
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot

function Get-ProductVersionText {
    $propsPath = Join-Path $root "Directory.Build.props"
    $text = [System.IO.File]::ReadAllText($propsPath)
    if ($text -notmatch '<Version>\s*([^<\s]+)\s*</Version>') {
        throw "Directory.Build.props 缺少 Version。"
    }
    return $Matches[1]
}

function Get-MinVersionText {
    if (-not [string]::IsNullOrWhiteSpace($MinVersion)) {
        return $MinVersion.Trim()
    }
    $path = Join-Path $PSScriptRoot "min-version.txt"
    $line = @(Get-Content -LiteralPath $path -Encoding UTF8 | Where-Object {
            $trim = $_.Trim()
            return $trim.Length -gt 0 -and -not $trim.StartsWith("#")
        } | Select-Object -First 1)
    if ($line.Count -lt 1) {
        throw "packaging/min-version.txt 没有版本号。"
    }
    return [string]$line[0]
}

function Get-AppxVersionText {
    $path = Join-Path $root "src\LanSpeed.App\Package.appxmanifest"
    [xml]$xml = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("m", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $node = $xml.SelectSingleNode("/m:Package/m:Identity", $ns)
    if (-not $node) {
        throw "Package.appxmanifest 缺少 Identity。"
    }
    return [string]$node.Version
}

function ConvertTo-AppVersion([string]$text) {
    $trimmed = $text.Trim()
    if ($trimmed -match '^[vV](\d+\.\d+\.\d+)$') {
        $trimmed = $Matches[1]
    }
    if ($trimmed -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "无法解析版本号：$text（需要 major.minor.patch）"
    }
    return New-Object System.Version ([int]$Matches[1]), ([int]$Matches[2]), ([int]$Matches[3])
}

function ConvertTo-AppxVersion([string]$text) {
    if ($text.Trim() -notmatch '^(\d+)\.(\d+)\.(\d+)\.(\d+)$') {
        throw "MSIX 版本必须是四段：$text"
    }
    if ($Matches[4] -ne "0") {
        throw "MSIX 版本第四段必须为 0：$text"
    }
    return New-Object System.Version ([int]$Matches[1]), ([int]$Matches[2]), ([int]$Matches[3])
}

function Assert-Amd64Project {
    $csproj = [System.IO.File]::ReadAllText((Join-Path $root "src\LanSpeed.App\LanSpeed.App.csproj"))
    if ($csproj -notmatch '<RuntimeIdentifier>\s*win-x64\s*</RuntimeIdentifier>') {
        throw "LanSpeed.App 的 RuntimeIdentifier 必须是 win-x64。"
    }
    if ($csproj -match 'win-arm64' -or $csproj -match '>win-arm<') {
        throw "不发布 ARM。LanSpeed.App 不能包含 ARM 运行时标识。"
    }
}

Assert-Amd64Project

$productText = if ([string]::IsNullOrWhiteSpace($Version)) { Get-ProductVersionText } else { $Version.Trim() }
$product = ConvertTo-AppVersion $productText
$minimum = ConvertTo-AppVersion (Get-MinVersionText)
$appx = ConvertTo-AppxVersion (Get-AppxVersionText)
$versionText = "$($product.Major).$($product.Minor).$($product.Build)"
$minText = "$($minimum.Major).$($minimum.Minor).$($minimum.Build)"

if ($appx -ne $product) {
    throw "lanspeed-version-mismatch: Package.appxmanifest 版本 $($appx.Major).$($appx.Minor).$($appx.Build) 与产品版本 $versionText 不一致。请把 Identity Version 改为 $versionText.0。"
}
if ($minimum -gt $product) {
    throw "lanspeed-min-too-high: 最低版本 $minText 高于正在发布的版本 $versionText。"
}

if ($CheckOnly) {
    Write-Output "lanspeed-ok $versionText min $minText amd64"
    exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    throw "未找到 GitHub CLI（gh）。请安装后执行 gh auth login。"
}
# auth status 在缺少 read:org 时返回非 0，但 repo 范围的令牌仍可发布。
& gh auth status
if ($LASTEXITCODE -ne 0) {
    & gh api user --jq .login | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "gh 未登录。请先执行 gh auth login，或设置 GH_TOKEN。"
    }
}

$project = Join-Path $root "src\LanSpeed.App\LanSpeed.App.csproj"
& dotnet build $project -c Release -r win-x64 --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Release 构建失败。"
}

$packages = Join-Path $root "packaging\AppPackages"
if (-not (Test-Path -LiteralPath $packages)) {
    throw "没有找到 packaging/AppPackages。Release 构建未产出 MSIX。"
}

$all = @(Get-ChildItem -Path $packages -Recurse -Filter *.msix)
$arm = @($all | Where-Object { $_.Name -match 'arm' })
if ($arm.Count -gt 0) {
    throw "发现 ARM 安装包，本产品不发布 ARM：$($arm[0].FullName)"
}

$appxFour = "$versionText.0"
$matched = @($all | Where-Object { $_.Name -match 'win-x64|_x64|-x64|amd64' -and $_.Name -like "*$appxFour*" })
if ($matched.Count -lt 1) {
    throw "没有找到版本 $appxFour 的 amd64 MSIX。"
}

$picked = $matched | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$canonical = "LanSpeed-$versionText-win-x64.msix"
if ($canonical -match 'arm') {
    throw "拒绝上传 ARM 包。"
}

$stage = Join-Path $env:TEMP ("lanspeed-publish-" + [guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $msixPath = Join-Path $stage $canonical
    Copy-Item -LiteralPath $picked.FullName -Destination $msixPath
    $jsonPath = Join-Path $stage "update.json"
    $notesPath = Join-Path $stage "notes.md"
    $manifest = [ordered]@{
        version    = $versionText
        minVersion = $minText
        arch       = "amd64"
        file       = $canonical
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($jsonPath, ($manifest | ConvertTo-Json), $utf8)
    $noteBody = "minVersion: $minText`r`n`r`n$Notes"
    [System.IO.File]::WriteAllText($notesPath, $noteBody, $utf8)

    $ghArgs = @(
        "release", "create", "v$versionText",
        "--repo", $Repo,
        "--title", "内网测速 $versionText",
        "--notes-file", $notesPath
    )
    if ($Draft) {
        $ghArgs += "--draft"
    }
    $ghArgs += @($msixPath, $jsonPath)
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) {
        throw "gh release create 失败。"
    }
    Write-Output "已发布 v$versionText（最低 $minText，仅 amd64）：$canonical"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
