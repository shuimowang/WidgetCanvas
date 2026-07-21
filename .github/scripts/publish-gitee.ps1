param(
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$ArtifactsDirectory
)

$ErrorActionPreference = 'Stop'
$owner = 'shuimowang'
$repo = 'WidgetCanvas'
$apiRoot = "https://gitee.com/api/v5/repos/$owner/$repo"
$headers = @{ 'User-Agent' = 'WidgetCanvas-release' }
$escapedToken = [Uri]::EscapeDataString($Token)
$artifactNames = @(
    'WidgetCanvas-win-x64.zip',
    'WidgetCanvas-win-x64.exe',
    'WidgetCanvas-win-x64.exe.sha256'
)

foreach ($name in $artifactNames) {
    $path = Join-Path $ArtifactsDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing release artifact: $path"
    }
    if ((Get-Item -LiteralPath $path).Length -gt 100MB) {
        throw "Gitee limits one attachment to 100 MB: $name"
    }
}

# Keep source, main and tags on Gitee aligned with the commit that GitHub Actions built.
$basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$owner`:$Token"))
git -c "http.extraHeader=Authorization: Basic $basic" push `
    "https://gitee.com/$owner/$repo.git" origin/main:refs/heads/main "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw 'Failed to mirror main and tag to Gitee.' }

$encodedTag = [Uri]::EscapeDataString($Tag)
$release = $null
try {
    $release = Invoke-RestMethod `
        -Uri "$apiRoot/releases/tags/$encodedTag`?access_token=$escapedToken" `
        -Headers $headers
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
}
if (-not $release -or -not $release.id) {
    $previousTag = git describe --tags --abbrev=0 "$Tag^" 2>$null
    $logRange = if ($LASTEXITCODE -eq 0 -and $previousTag) { "$previousTag..$Tag" } else { $Tag }
    $changes = git log $logRange --pretty='- %s' --no-merges
    $body = if ($changes) {
        "## 更新内容`n`n" + ($changes -join "`n") + "`n`n完整历史请查看项目提交记录。"
    }
    else {
        '完整历史请查看项目提交记录。'
    }
    $release = Invoke-RestMethod -Method Post -Uri "$apiRoot/releases" -Headers $headers -Body @{
        access_token = $Token
        tag_name = $Tag
        target_commitish = 'main'
        name = "WidgetCanvas $Tag"
        body = $body
        prerelease = 'false'
    }
}

$existing = @(Invoke-RestMethod `
    -Uri "$apiRoot/releases/$($release.id)/attach_files?access_token=$escapedToken" `
    -Headers $headers)
foreach ($asset in $existing) {
    if ($artifactNames -contains $asset.name) {
        Invoke-RestMethod -Method Delete `
            -Uri "$apiRoot/releases/$($release.id)/attach_files/$($asset.id)?access_token=$escapedToken" `
            -Headers $headers | Out-Null
    }
}

foreach ($name in $artifactNames) {
    $file = Get-Item -LiteralPath (Join-Path $ArtifactsDirectory $name)
    Invoke-RestMethod -Method Post -Uri "$apiRoot/releases/$($release.id)/attach_files" `
        -Headers $headers -Form @{ access_token = $Token; file = $file } | Out-Null
}

# The repository attachment quota is finite. Keep binaries for the newest four
# releases while preserving older tags, release notes and source archives.
$releases = @(Invoke-RestMethod `
    -Uri "$apiRoot/releases?per_page=100&page=1&access_token=$escapedToken" `
    -Headers $headers)
$oldReleases = $releases | Where-Object { -not $_.prerelease } |
    Sort-Object { [DateTimeOffset]$_.created_at } -Descending | Select-Object -Skip 4
foreach ($oldRelease in $oldReleases) {
    $oldAssets = @(Invoke-RestMethod `
        -Uri "$apiRoot/releases/$($oldRelease.id)/attach_files?access_token=$escapedToken" `
        -Headers $headers)
    foreach ($asset in $oldAssets) {
        Invoke-RestMethod -Method Delete `
            -Uri "$apiRoot/releases/$($oldRelease.id)/attach_files/$($asset.id)?access_token=$escapedToken" `
            -Headers $headers | Out-Null
    }
}

Write-Host "Published $Tag and $($artifactNames.Count) assets to Gitee."
