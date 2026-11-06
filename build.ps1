# 大肥鱼小说下载器 · 一键构建
# 用法：.\build.ps1            （构建宿主 + 安装包）
#       .\build.ps1 -SkipInstaller  （只构建宿主，跳过安装包）
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$Root   = $PSScriptRoot
$Csc    = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$Wv2    = Join-Path $Root 'vendor\webview2'
$Dist   = Join-Path $Root 'dist'
$Build  = Join-Path $Root 'build'
$Stage  = Join-Path $Root 'build\stage'
$SevenZ = 'C:\Program Files\7-Zip\7z.exe'

function Need($cond, $msg) { if (-not $cond) { throw $msg } }
function Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

Step '检查前置条件'
Need (Test-Path $Csc)  "找不到 csc.exe：$Csc"
Need (Test-Path (Join-Path $Wv2 'Microsoft.Web.WebView2.Core.dll'))    '缺少 vendor\webview2\Microsoft.Web.WebView2.Core.dll'
Need (Test-Path (Join-Path $Wv2 'Microsoft.Web.WebView2.WinForms.dll')) '缺少 vendor\webview2\Microsoft.Web.WebView2.WinForms.dll'
Need (Test-Path (Join-Path $Wv2 'WebView2Loader.dll'))                  '缺少 vendor\webview2\WebView2Loader.dll'
Need (Test-Path (Join-Path $Dist '番茄小说下载器.exe'))                  '缺少后端引擎 dist\番茄小说下载器.exe（从上游 Releases 下载）'
Need (Test-Path (Join-Path $Dist 'nd\nd_bridge\nd_bridge.exe'))         '缺少多书源引擎 dist\nd\nd_bridge\nd_bridge.exe（见 docs\BUILD.md）'

foreach ($d in @($Dist, $Build, $Stage)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

# 结束可能占用输出文件的进程
Get-Process -Name '大肥鱼小说下载器' -ErrorAction SilentlyContinue | Stop-Process -Force

Step '构建宿主'
$hostExe = Join-Path $Dist '大肥鱼小说下载器.exe'
$args = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    "/out:$hostExe",
    "/win32icon:$(Join-Path $Root 'ui\favicon.ico')",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    "/r:$(Join-Path $Wv2 'Microsoft.Web.WebView2.Core.dll')",
    "/r:$(Join-Path $Wv2 'Microsoft.Web.WebView2.WinForms.dll')",
    (Join-Path $Root 'src\host\Program.cs')
)
& $Csc @args
Need ($LASTEXITCODE -eq 0) '宿主编译失败'
Write-Host "    产出 $hostExe ($((Get-Item $hostExe).Length) 字节)"

Step '整理运行目录'
Copy-Item (Join-Path $Wv2 '*.dll') $Dist -Force
Copy-Item (Join-Path $Root 'ui')    $Dist -Recurse -Force
Copy-Item (Join-Path $Dist 'nd\nd_bridge') $Dist -Recurse -Force

if ($SkipInstaller) { Write-Host '已跳过安装包构建' -ForegroundColor Yellow; return }

Step '生成安装包载荷'
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path $Stage -Force | Out-Null
Copy-Item (Join-Path $Dist '大肥鱼小说下载器.exe') $Stage -Force
Copy-Item (Join-Path $Dist '*.dll') $Stage -Force
Copy-Item (Join-Path $Dist '番茄小说下载器.exe') $Stage -Force
Copy-Item (Join-Path $Dist 'ui') $Stage -Recurse -Force
Copy-Item (Join-Path $Dist 'nd') $Stage -Recurse -Force
Copy-Item (Join-Path $Root 'docs\安装必读.txt') $Stage -Force
Copy-Item (Join-Path $Root 'THIRD-PARTY-NOTICES.txt') $Stage -Force

$zip = Join-Path $Build 'payload.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Need (Test-Path $SevenZ) '找不到 7z.exe，请安装 7-Zip 或修改脚本中的 $SevenZ 路径'
& $SevenZ a -tzip -mx=9 -mm=Deflate $zip "$Stage\*" | Out-Null
Write-Host "    载荷 $([math]::Round((Get-Item $zip).Length / 1MB, 2)) MB"

Step '构建安装程序'
$instExe = Join-Path $Dist '大肥鱼小说下载器安装程序.exe'
Push-Location $Root
& $Csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$instExe" `
    "/win32icon:$(Join-Path $Root 'ui\favicon.ico')" "/resource:$zip" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    (Join-Path $Root 'src\installer\Installer.cs')
Pop-Location
Need ($LASTEXITCODE -eq 0) '安装程序编译失败'
Write-Host "    产出 $instExe ($([math]::Round((Get-Item $instExe).Length / 1MB, 2)) MB)"

Write-Host ''
Write-Host '构建完成。' -ForegroundColor Green