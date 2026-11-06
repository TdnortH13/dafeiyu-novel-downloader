using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

static class Installer
{
    public const string AppName = "大肥鱼小说下载器";
    public const string AppExe = "大肥鱼小说下载器.exe";
    public const string BackendExe = "番茄小说下载器.exe";

    public static string DefaultDir
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "大肥鱼小说下载器");
        }
    }

    public static string DesktopDir
    {
        get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
    }

    public static string StartMenuDir
    {
        get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); }
    }

    [STAThread]
    static void Main(string[] args)
    {
        bool silent = false;
        string dir = null;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "/S" || a == "-s" || a == "--silent") silent = true;
            else if (a.StartsWith("/D=")) dir = a.Substring(3).Trim('"');
        }
        if (dir == null || dir.Length == 0) dir = DefaultDir;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (silent)
        {
            try
            {
                InstallCore(dir, null, null);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "install_error.txt"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex.ToString(),
                        new System.Text.UTF8Encoding(false));
                }
                catch { }
                Environment.Exit(1);
            }
            return;
        }

        Application.Run(new InstallForm(dir));
    }

    // ---------- 核心安装 ----------
    public static void InstallCore(string targetDir, ProgressBar bar, Label status)
    {
        Directory.CreateDirectory(targetDir);

        Assembly asm = Assembly.GetExecutingAssembly();
        string resName = null;
        string[] names = asm.GetManifestResourceNames();
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == "payload.zip" || names[i].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                resName = names[i];
                break;
            }
        }
        if (resName == null) throw new Exception("安装包损坏：找不到内部数据。已含资源：" + string.Join(",", names));

        using (Stream raw = asm.GetManifestResourceStream(resName))
        {
            if (raw == null) throw new Exception("安装包损坏：无法读取内部数据。");

            // 先落盘临时文件，ZipArchive 需要可随机访问（放在安装目录内，避免 %TEMP% 不可写）
            string tmp = Path.Combine(targetDir, ".payload_" + Guid.NewGuid().ToString("N") + ".tmp");
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = raw.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }

            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(tmp))
                {
                    int total = zip.Entries.Count;
                    int done = 0;
                    if (bar != null)
                    {
                        bar.Minimum = 0;
                        bar.Maximum = total;
                        bar.Value = 0;
                    }

                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        done++;
                        string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string dest = Path.Combine(targetDir, rel);

                        if (e.FullName.EndsWith("/"))
                        {
                            Directory.CreateDirectory(dest);
                        }
                        else
                        {
                            string parent = Path.GetDirectoryName(dest);
                            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                            e.ExtractToFile(dest, true);
                        }

                        if (bar != null)
                        {
                            if (done <= bar.Maximum) bar.Value = done;
                            if (status != null && (done % 20 == 0 || done == total))
                            {
                                status.Text = "正在解压 " + done + " / " + total + " 个文件…";
                                Application.DoEvents();
                            }
                        }
                    }
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        if (status != null) { status.Text = "正在创建快捷方式…"; Application.DoEvents(); }
        MakeShortcuts(targetDir);
        WriteUninstaller(targetDir);
    }

    public static void MakeShortcuts(string targetDir)
    {
        string exe = Path.Combine(targetDir, AppExe);
        try
        {
            CreateShortcut(Path.Combine(DesktopDir, AppName + ".lnk"), exe, targetDir, exe + ",0");
        }
        catch { }

        try
        {
            string sm = Path.Combine(StartMenuDir, "大肥鱼小说下载器");
            Directory.CreateDirectory(sm);
            CreateShortcut(Path.Combine(sm, AppName + ".lnk"), exe, targetDir, exe + ",0");
            CreateShortcut(Path.Combine(sm, "卸载.lnk"), Path.Combine(targetDir, "卸载.cmd"), targetDir, exe + ",0");
        }
        catch { }
    }

    public static void CreateShortcut(string lnkPath, string target, string workDir, string icon)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) throw new Exception("WScript.Shell 不可用");
        object shell = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
        Type st = sc.GetType();
        st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
        st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { icon });
        st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { AppName });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    public static void WriteUninstaller(string targetDir)
    {
        string[] lines = new string[]
        {
            "@echo off",
            "chcp 65001 >nul",
            "set \"D=%~dp0\"",
            "del /f /q \"%USERPROFILE%\\Desktop\\" + AppName + ".lnk\" >nul 2>&1",
            "rd /s /q \"%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\大肥鱼小说下载器\" >nul 2>&1",
            "del /f /q \"%LOCALAPPDATA%\\TomatoNovelDesktop\\startup.log\" >nul 2>&1",
            "echo 正在卸载 " + AppName + " ...",
            "cd /d \"%TEMP%\"",
            "start \"\" /b cmd /c \"timeout /t 2 >nul & rd /s /q \\\"%D:~0,-1%\\\"\"",
            "exit"
        };
        File.WriteAllText(Path.Combine(targetDir, "卸载.cmd"), string.Join("\r\n", lines) + "\r\n",
            new System.Text.UTF8Encoding(true));
    }
}

class InstallForm : Form
{
    private TextBox pathBox;
    private Button browseBtn;
    private Button installBtn;
    private Button runBtn;
    private Button closeBtn;
    private ProgressBar bar;
    private Label status;
    private CheckBox runAfter;
    private string installedExe;

    public InstallForm(string dir)
    {
        this.Text = Installer.AppName + " 安装程序";
        this.ClientSize = new Size(560, 330);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(246, 247, 250);
        this.Font = new Font("Microsoft YaHei UI", 9f);

        Label title = new Label();
        title.Text = "大肥鱼小说下载器";
        title.Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(26, 32, 48);
        title.SetBounds(24, 20, 320, 34);
        this.Controls.Add(title);

        Label sub = new Label();
        sub.Text = "多书源小说下载器 · 桌面版（已内置运行环境，无需另装 Python）";
        sub.ForeColor = Color.FromArgb(110, 118, 136);
        sub.SetBounds(26, 58, 500, 20);
        this.Controls.Add(sub);

        Label lb = new Label();
        lb.Text = "安装到：";
        lb.SetBounds(26, 104, 60, 22);
        this.Controls.Add(lb);

        pathBox = new TextBox();
        pathBox.Text = dir;
        pathBox.SetBounds(88, 101, 360, 24);
        this.Controls.Add(pathBox);

        browseBtn = new Button();
        browseBtn.Text = "浏览…";
        browseBtn.SetBounds(456, 100, 78, 26);
        browseBtn.Click += delegate
        {
            FolderBrowserDialog f = new FolderBrowserDialog();
            f.SelectedPath = pathBox.Text;
            if (f.ShowDialog() == DialogResult.OK) pathBox.Text = f.SelectedPath;
        };
        this.Controls.Add(browseBtn);

        bar = new ProgressBar();
        bar.SetBounds(26, 148, 508, 14);
        this.Controls.Add(bar);

        status = new Label();
        status.Text = "准备就绪，点击「开始安装」。";
        status.ForeColor = Color.FromArgb(110, 118, 136);
        status.SetBounds(26, 168, 508, 20);
        this.Controls.Add(status);

        runAfter = new CheckBox();
        runAfter.Text = "安装完成后立即启动";
        runAfter.Checked = true;
        runAfter.SetBounds(26, 200, 220, 22);
        this.Controls.Add(runAfter);

        installBtn = new Button();
        installBtn.Text = "开始安装";
        installBtn.SetBounds(286, 250, 118, 36);
        installBtn.BackColor = Color.FromArgb(232, 71, 47);
        installBtn.ForeColor = Color.White;
        installBtn.FlatStyle = FlatStyle.Flat;
        installBtn.FlatAppearance.BorderSize = 0;
        installBtn.Click += new EventHandler(this.OnInstall);
        this.Controls.Add(installBtn);

        runBtn = new Button();
        runBtn.Text = "立即运行";
        runBtn.SetBounds(150, 250, 118, 36);
        runBtn.Enabled = false;
        runBtn.Click += delegate
        {
            if (this.installedExe != null && File.Exists(this.installedExe))
                Process.Start(new ProcessStartInfo(this.installedExe) { WorkingDirectory = Path.GetDirectoryName(this.installedExe) });
        };
        this.Controls.Add(runBtn);

        closeBtn = new Button();
        closeBtn.Text = "关闭";
        closeBtn.SetBounds(414, 250, 118, 36);
        closeBtn.Click += delegate { this.Close(); };
        this.Controls.Add(closeBtn);
    }

    private void OnInstall(object sender, EventArgs e)
    {
        string dir = this.pathBox.Text.Trim();
        if (dir.Length == 0) { MessageBox.Show(this, "请先选择安装目录。", Installer.AppName); return; }

        try
        {
            if (Directory.Exists(dir))
            {
                string probe = Path.Combine(dir, Installer.AppExe);
                if (File.Exists(probe))
                {
                    if (MessageBox.Show(this, "该目录下已安装过，是否覆盖安装？", Installer.AppName,
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                }
            }
        }
        catch { }

        this.installBtn.Enabled = false;
        this.browseBtn.Enabled = false;
        this.pathBox.Enabled = false;
        this.status.Text = "正在解压文件…";
        Application.DoEvents();

        try
        {
            Installer.InstallCore(dir, this.bar, this.status);
            this.installedExe = Path.Combine(dir, Installer.AppExe);
            this.bar.Value = this.bar.Maximum;
            this.status.Text = "安装完成！已创建桌面快捷方式。";
            this.runBtn.Enabled = true;
            this.closeBtn.Text = "完成";

            if (this.runAfter.Checked)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(this.installedExe) { WorkingDirectory = dir });
                    this.Close();
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            this.status.Text = "安装失败：" + ex.Message;
            MessageBox.Show(this, "安装失败：\r\n" + ex.Message, Installer.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            this.installBtn.Enabled = true;
            this.browseBtn.Enabled = true;
            this.pathBox.Enabled = true;
        }
    }
}
