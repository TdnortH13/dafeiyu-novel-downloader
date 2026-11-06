# 编译与打包说明

本文说明如何从源码构建出可运行的桌面程序与安装包。

## 前置条件

| 依赖 | 说明 |
|---|---|
| Windows 10/11 x64 | 目标平台 |
| .NET Framework 4.x | 系统自带，用其 `csc.exe` 编译，**无需** .NET SDK |
| Python 3.11+ | 仅用于构建多书源引擎 |
| 7-Zip | 仅用于生成安装包载荷（`7z.exe`） |
| WebView2 SDK | 需 `Microsoft.Web.WebView2.Core.dll` 等三个文件 |

### csc.exe 路径

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

宿主代码按 **C# 5** 语法编写（不使用字符串插值、`?.`、表达式体成员、
`nameof`），以便用系统自带的 csc 直接编译。

### 获取 WebView2 SDK

从 NuGet 下载 `Microsoft.Web.WebView2`，解包后取三个文件：

```
lib\net462\Microsoft.Web.WebView2.Core.dll
lib\net462\Microsoft.Web.WebView2.WinForms.dll
runtimes\win-x64\native\WebView2Loader.dll
```

放到 `vendor\webview2\` 目录。

## 构建步骤

### 1. 番茄后端引擎

从上游 Releases 下载 `TomatoNovelDownloader-Win64-*.exe`，
重命名为 `番茄小说下载器.exe`，放到 `dist\` 目录。

> 宿主的查找顺序：环境变量 `TOMATO_BACKEND_EXE` → 程序同目录 →
> 上一级目录。

### 2. 多书源引擎

```powershell
pip install novel-downloader pyinstaller

python -m PyInstaller --noconfirm --onedir --console --name nd_bridge `
    --collect-all novel_downloader `
    --distpath dist\nd --workpath build\pyi --specpath build\pyi `
    bridge\nd_bridge.py
```

`--collect-all novel_downloader` 是**必需**的：书源插件在运行时动态导入，
PyInstaller 的静态分析发现不了，不加会缺插件与资源文件。

产物为 `dist\nd\nd_bridge\nd_bridge.exe`（约 38 MB，含 Python 运行时）。

### 3. 图标（可选）

```powershell
csc /nologo /target:exe /platform:x64 /out:MakeIcon.exe `
    /r:System.dll /r:System.Drawing.dll src\tools\MakeIcon.cs

MakeIcon.exe app_icon.png favicon.ico
```

`MakeIcon` 会生成 16/24/32/48/64/128/256 七档 ICO：
小尺寸用 DIB 条目（兼容性最好），128 以上用 PNG 条目（体积小）。

### 4. 宿主程序

```powershell
csc /nologo /target:winexe /platform:x64 /optimize+ `
    /out:dist\大肥鱼小说下载器.exe `
    /win32icon:ui\favicon.ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:vendor\webview2\Microsoft.Web.WebView2.Core.dll `
    /r:vendor\webview2\Microsoft.Web.WebView2.WinForms.dll `
    src\host\Program.cs
```

运行时目录结构（三者必须同目录）：

```
大肥鱼小说下载器.exe
Microsoft.Web.WebView2.Core.dll
Microsoft.Web.WebView2.WinForms.dll
WebView2Loader.dll
ui\           ← 前端三件套 + 图标
nd\           ← 多书源引擎
番茄小说下载器.exe
```

### 5. 安装包

把上面整套目录结构打成 zip：

```powershell
7z a -tzip -mx=9 -mm=Deflate payload.zip .\stage\*
```

然后以嵌入资源的方式编译安装程序：

```powershell
csc /nologo /target:winexe /platform:x64 /optimize+ `
    /out:大肥鱼小说下载器安装程序.exe `
    /win32icon:ui\favicon.ico /resource:payload.zip `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    src\installer\Installer.cs
```

安装程序支持命令行：

```
大肥鱼小说下载器安装程序.exe            交互式安装
大肥鱼小说下载器安装程序.exe /S         静默安装
大肥鱼小说下载器安装程序.exe /S /D=D:\App   静默安装到指定目录
```

> **注意**：载荷必须用 **zip + Deflate**，因为解压端是 .NET 的 `ZipFile`，
> 不支持 7z/LZMA。

## 调试

宿主支持一个自检开关，启动后自动切换 7 个页面并记录每页的报错：

```powershell
$env:TOMATO_SELFTEST = "1"
.\大肥鱼小说下载器.exe
```

日志位置（按优先级回退）：

```
%LOCALAPPDATA%\DaFeiYuNovel\startup.log
<程序目录>\data\startup.log
%TEMP%\startup.log
```

## 常见坑

- **`SetVirtualHostNameToFolderMapping` 会让 `WebResourceRequested` 失效**：
  注册虚拟主机映射后，该事件一次都不会触发，页面请求全部失败。
  本项目因此**不使用**该映射，静态文件也由 `WebResourceRequested` 自己读盘返回。
- **中文文件名传给 signtool 会被截断**：签名前先复制成 ASCII 临时文件名。
- **`con` / `prn` / `aux` / `nul` 是 Windows 保留设备名**，不能用作源文件名。