# 一键推送到 GitHub
# 用法：
#   .\push.ps1 -RepoUrl https://github.com/你的用户名/大肥鱼小说下载器.git
#   或直接 .\push.ps1 然后按提示粘贴地址
param(
    [string]$RepoUrl,
    [string]$Branch = 'main'
)

$ErrorActionPreference = 'Stop'
$git = 'C:\Program Files\Git\cmd\git.exe'
if (-not (Test-Path $git)) { $git = 'git' }

Set-Location $PSScriptRoot

if (-not $RepoUrl) {
    Write-Host '请粘贴你的 GitHub 仓库地址（例如 https://github.com/xxx/dafeiyu-novel-downloader.git）：'
    $RepoUrl = Read-Host
}
if (-not $RepoUrl) { throw '未提供仓库地址' }

Write-Host '==> 安全检查：确认没有敏感文件被跟踪' -ForegroundColor Cyan
$bad = & $git ls-files | Where-Object { $_ -match '\.(pfx|p12|snk|cer)$|payload\.zip|^dist/|^build/|^stage/' }
if ($bad) {
    Write-Host '发现不该提交的文件，已中止：' -ForegroundColor Red
    $bad | ForEach-Object { Write-Host "  $_" }
    throw '请先把这些文件加入 .gitignore 并从索引移除'
}
Write-Host '    通过' -ForegroundColor Green

$existing = & $git remote 2>$null
if ($existing -contains 'origin') {
    & $git remote set-url origin $RepoUrl
} else {
    & $git remote add origin $RepoUrl
}

Write-Host "==> 推送到 $RepoUrl ($Branch)" -ForegroundColor Cyan
& $git push -u origin $Branch

Write-Host ''
Write-Host '完成。' -ForegroundColor Green