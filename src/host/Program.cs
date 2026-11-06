using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TomatoDesktop
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write_test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static string PickDir()
        {
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(lad))
            {
                string preferred = Path.Combine(lad, "DaFeiYuNovel");
                if (CanWrite(preferred)) return preferred;
            }
            string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (CanWrite(portable)) return portable;
            return Path.GetTempPath();
        }

        internal static readonly string LogDir = PickDir();

        internal static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(LogDir, "startup.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n");
            }
            catch { }
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Log("UNHANDLED: " + (e.ExceptionObject == null ? "null" : e.ExceptionObject.ToString()));
        }

        private static void OnThread(object sender, ThreadExceptionEventArgs e)
        {
            Log("THREAD-EX: " + e.Exception);
        }

        [STAThread]
        private static void Main()
        {
            Log("=== 启动 ===");
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application.ThreadException += OnThread;
            try
            {
                try
                {
                    if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware();
                }
                catch { try { SetProcessDPIAware(); } catch { } }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log("创建主窗口");
                MainForm form = new MainForm();
                Log("进入消息循环");
                Application.Run(form);
                Log("=== 正常退出 ===");
            }
            catch (Exception ex)
            {
                Log("FATAL: " + ex);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly WebView2 web = new WebView2();
        private readonly Label errorLabel = new Label();
        private readonly StringBuilder backendOut = new StringBuilder();
        private Process server;
        private bool ownServer;
        private string host;
        private int port;
        private string baseUrl;

        // 自研前端：虚拟主机 + API 反向代理
        private const string UiHost = "tomato.local";
        private const string UiOrigin = "https://tomato.local";
        private static readonly CookieContainer proxyCookies = new CookieContainer();
        private string uiDir;
        private bool useUi;

        public MainForm()
        {
            Program.Log("MainForm ctor");
            this.Text = "大肥鱼小说下载器";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(1280, 840);
            this.MinimumSize = new Size(900, 620);
            this.BackColor = Color.FromArgb(240, 242, 245);

            this.ResolveEndpoint();
            Program.Log("endpoint = " + this.baseUrl);
            this.TrySetIcon();

            this.web.Dock = DockStyle.Fill;
            this.Controls.Add(this.web);

            this.errorLabel.Dock = DockStyle.Fill;
            this.errorLabel.TextAlign = ContentAlignment.MiddleCenter;
            this.errorLabel.BackColor = Color.FromArgb(250, 251, 252);
            this.errorLabel.ForeColor = Color.FromArgb(90, 90, 110);
            this.errorLabel.Font = new Font("Microsoft YaHei UI", 10.5f);
            this.errorLabel.Padding = new Padding(48);
            this.errorLabel.Visible = false;
            this.Controls.Add(this.errorLabel);
            Program.Log("MainForm ctor done");
        }

        private void ShowError(string heading, string detail)
        {
            Program.Log("ERROR UI: " + heading + " / " + detail);
            try
            {
                this.errorLabel.Text = heading + "\r\n\r\n" + detail +
                    "\r\n\r\n日志：" + Program.LogDir;
                this.errorLabel.Visible = true;
                this.errorLabel.BringToFront();
                this.Text = "大肥鱼小说下载器";
            }
            catch { }
        }

        private void ResolveEndpoint()
        {
            string raw = Environment.GetEnvironmentVariable("TOMATO_WEB_ADDR");
            string addr = null;
            if (!string.IsNullOrEmpty(raw))
            {
                string first = raw.Split(',')[0].Trim();
                if (first.Length > 0) addr = first;
            }
            if (addr == null) addr = "127.0.0.1:18423";

            int idx = addr.LastIndexOf(':');
            if (idx > 0)
            {
                this.host = addr.Substring(0, idx).Trim('[', ']');
                int p;
                if (!int.TryParse(addr.Substring(idx + 1), out p)) p = 18423;
                this.port = p;
            }
            else
            {
                this.host = "127.0.0.1";
                this.port = 18423;
            }

            if (this.host == "0.0.0.0" || this.host == "::" || this.host == "*" || this.host.Length == 0)
            {
                this.host = "127.0.0.1";
            }
            this.baseUrl = "http://" + this.host + ":" + this.port + "/";
        }

        private void TrySetIcon()
        {
            try
            {
                Icon ic = null;
                try { ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch (Exception ex1) { Program.Log("own icon fail: " + ex1.Message); }
                if (ic == null)
                {
                    string exe = ResolveDownloaderExe();
                    if (exe != null) ic = Icon.ExtractAssociatedIcon(exe);
                }
                if (ic != null) this.Icon = ic;
            }
            catch (Exception ex) { Program.Log("icon fail: " + ex.Message); }
        }

        private static string ResolveDownloaderExe()
        {
            List<string> candidates = new List<string>();
            string env = Environment.GetEnvironmentVariable("TOMATO_BACKEND_EXE");
            if (!string.IsNullOrEmpty(env)) candidates.Add(env);
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "番茄小说下载器.exe"));
            try
            {
                DirectoryInfo parent = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
                if (parent != null) candidates.Add(Path.Combine(parent.FullName, "番茄小说下载器.exe"));
            }
            catch { }
            for (int i = 0; i < candidates.Count; i++)
            {
                if (File.Exists(candidates[i])) return candidates[i];
            }
            return null;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                await BootAsync();
            }
            catch (Exception ex)
            {
                Program.Log("BOOT FAIL: " + ex);
                this.ShowError("启动失败", ex.Message);
            }
        }

        private async Task BootAsync()
        {
            string exe = ResolveDownloaderExe();
            Program.Log("downloader exe = " + (exe == null ? "(null)" : exe));
            if (exe == null)
            {
                this.ShowError("找不到后端程序 番茄小说下载器.exe",
                    "请把「番茄小说下载器.exe」与本程序放在同一目录，或设置环境变量 TOMATO_BACKEND_EXE 指向它。");
                return;
            }

            if (!await Task.Run(() => this.WaitHttp(2000)))
            {
                Program.Log("后端未运行，自行拉起");
                this.Text = "大肥鱼小说下载器 · 正在启动后端…";
                bool ok = await Task.Run(() => this.StartServerAndWait(exe, 45000));
                Program.Log("后端就绪 = " + ok);
                if (!ok)
                {
                    string why = "等待 45 秒仍无响应。";
                    bool died = false;
                    try { died = (this.server != null && this.server.HasExited); } catch { }
                    if (died) why = "后端进程启动后立即退出了。";
                    string tail = this.BackendTail();
                    if (tail.Length > 0) why = why + "\r\n\r\n" + tail;
                    this.ShowError("后端服务启动失败", why);
                    return;
                }
            }
            else
            {
                Program.Log("复用已运行的后端");
            }

            this.Text = "大肥鱼小说下载器 · 正在加载界面…";

            string udf = Path.Combine(Program.LogDir, "WebView2");
            Program.Log("创建 WebView2 环境, udf=" + udf);
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, udf, null);
            Program.Log("EnsureCoreWebView2Async");
            await this.web.EnsureCoreWebView2Async(env);
            Program.Log("CoreWebView2 ready");

            CoreWebView2 cw = this.web.CoreWebView2;
            cw.Settings.IsStatusBarEnabled = false;
            cw.Settings.AreDevToolsEnabled = false;
            cw.Settings.AreDefaultContextMenusEnabled = true;
            cw.Settings.IsZoomControlEnabled = true;
            cw.NewWindowRequested += this.OnNewWindowRequested;

            // 优先使用随程序部署的自研前端（ui 目录）
            this.uiDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            this.useUi = File.Exists(Path.Combine(this.uiDir, "index.html"));
            Program.Log("ui dir = " + this.uiDir + " , useUi = " + this.useUi);

            if (this.useUi)
            {
                try
                {
                    // 注意：虚拟主机映射会吃掉 WebResourceRequested，故不使用映射，
                    // 静态文件与 API 全部由 OnWebResourceRequested 自己提供。
                    cw.AddWebResourceRequestedFilter(UiOrigin + "/*",
                        CoreWebView2WebResourceContext.All);
                    cw.WebResourceRequested += this.OnWebResourceRequested;
                    cw.WebMessageReceived += this.OnWebMessageReceived;
                    cw.NavigationCompleted += this.OnUiNavigationCompleted;
                    cw.Navigate(UiOrigin + "/index.html");
                    this.Text = "大肥鱼小说下载器";
                    Program.Log("navigate -> " + UiOrigin + "/index.html (自研前端)");
                    return;
                }
                catch (Exception ex)
                {
                    Program.Log("自研前端挂载失败，回退内置界面: " + ex.Message);
                    this.useUi = false;
                }
            }

            cw.Navigate(this.baseUrl);
            this.Text = "大肥鱼小说下载器";
            Program.Log("navigate -> " + this.baseUrl);
        }

        // ---- 自研前端：导航结果自检 ----
        private async void OnUiNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            Program.Log("nav completed: success=" + e.IsSuccess + " status=" + e.WebErrorStatus);
            if (!e.IsSuccess)
            {
                Program.Log("自研前端导航失败，回退内置界面");
                try
                {
                    this.useUi = false;
                    this.web.CoreWebView2.Navigate(this.baseUrl);
                }
                catch (Exception ex) { Program.Log("fallback nav fail: " + ex.Message); }
                return;
            }

            await Task.Delay(1800);
            try
            {
                string probe = await this.web.CoreWebView2.ExecuteScriptAsync(
                    "JSON.stringify({href:location.href,t:document.title," +
                    "hl:document.documentElement.outerHTML.length," +
                    "theme:document.documentElement.getAttribute('data-theme')," +
                    "hasFn:typeof loadStatus," +
                    "bg:getComputedStyle(document.body).backgroundColor," +
                    "accent:getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()," +
                    "railW:(document.querySelector('.rail')?document.querySelector('.rail').offsetWidth:0)," +
                    "cards:document.querySelectorAll('.stat').length," +
                    "bt:(document.body?document.body.innerText:'').slice(0,120)})");
                Program.Log("dom probe: " + probe);
            }
            catch (Exception ex) { Program.Log("dom probe fail: " + ex.Message); }

            if (Environment.GetEnvironmentVariable("TOMATO_SELFTEST") == "1")
            {
                await this.RunSelfTest();
            }
        }

        // 逐页切换，检查是否报错（仅 TOMATO_SELFTEST=1 时启用）
        private async Task RunSelfTest()
        {
            string[] pages = new string[] { "search", "jobs", "history", "updates", "library", "config", "status" };
            for (int i = 0; i < pages.Length; i++)
            {
                try
                {
                    await this.web.CoreWebView2.ExecuteScriptAsync("location.hash='#" + pages[i] + "'");
                    await Task.Delay(1500);
                    string r = await this.web.CoreWebView2.ExecuteScriptAsync(
                        "JSON.stringify({p:'" + pages[i] + "',len:document.body.innerText.length," +
                        "errs:(window.__errs||[]).slice(0,6)})");
                    Program.Log("selftest " + r);
                }
                catch (Exception ex) { Program.Log("selftest fail " + pages[i] + ": " + ex.Message); }
            }

            // 多书源代理链路实测（走 WebView2 内的同源 fetch -> C# 代理 -> nd_bridge.exe）
            try
            {
                await this.web.CoreWebView2.ExecuteScriptAsync(
                    "(async()=>{try{window.__multi={stage:'start'};" +
                    "const a=await fetch('/api/multi/sites').then(r=>r.json());" +
                    "window.__multi.sites=a.count;" +
                    "const b=await fetch('/api/multi/search?q='+encodeURIComponent('斗破苍穹')+'&sites=quanben5').then(r=>r.json());" +
                    "window.__multi.items=(b.items||[]).length;" +
                    "window.__multi.first=((b.items||[])[0]||{}).title||'';" +
                    "window.__multi.stage='done';}catch(e){window.__multi.err=String(e);}})()");
                await Task.Delay(14000);
                string m = await this.web.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__multi||{})");
                Program.Log("multi probe " + m);
            }
            catch (Exception ex) { Program.Log("multi probe fail: " + ex.Message); }
        }

        // ---- 自研前端：把 https://tomato.local/api/* 反向代理到本地后端 ----
        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string uri = e.Request.Uri;
            if (uri == null || !uri.StartsWith(UiOrigin + "/")) return;

            string rel = uri.Substring(UiOrigin.Length); // 含 query
            int qi = rel.IndexOf('?');
            string pathOnly = qi >= 0 ? rel.Substring(0, qi) : rel;
            if (pathOnly.Length == 0 || pathOnly == "/") pathOnly = "/index.html";

            if (!pathOnly.StartsWith("/api/"))
            {
                this.ServeStatic(e, pathOnly);
                return;
            }

            if (pathOnly.StartsWith("/api/multi/"))
            {
                string mq = qi >= 0 ? rel.Substring(qi + 1) : "";
                this.HandleMulti(e, pathOnly, mq);
                return;
            }

            string method = e.Request.Method;
            CoreWebView2Deferral deferral = e.GetDeferral();

            string bodyText = null;
            try
            {
                if (e.Request.Content != null)
                {
                    using (StreamReader sr = new StreamReader(e.Request.Content, Encoding.UTF8))
                    {
                        bodyText = sr.ReadToEnd();
                    }
                }
            }
            catch (Exception ex) { Program.Log("proxy read body fail: " + ex.Message); }

            List<string[]> hdrs = new List<string[]>();
            try
            {
                foreach (KeyValuePair<string, string> kv in e.Request.Headers)
                {
                    hdrs.Add(new string[] { kv.Key, kv.Value });
                }
            }
            catch { }

            string target = this.baseUrl.TrimEnd('/') + rel;

            Task.Run(delegate
            {
                byte[] data = new byte[0];
                int status = 502;
                string ctype = "application/json; charset=utf-8";
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(target);
                    req.Method = method;
                    req.AllowAutoRedirect = false;
                    req.Timeout = 180000;
                    req.ReadWriteTimeout = 180000;
                    req.Proxy = null;
                    req.KeepAlive = false;
                    req.CookieContainer = proxyCookies;

                    for (int i = 0; i < hdrs.Count; i++)
                    {
                        string k = hdrs[i][0].ToLowerInvariant();
                        if (k == "host" || k == "content-length" || k == "connection" ||
                            k == "cookie" || k == "origin" || k == "referer" ||
                            k == "content-type" || k == "accept-encoding") continue;
                        try { req.Headers[k] = hdrs[i][1]; } catch { }
                    }

                    if (bodyText != null && method != "GET" && method != "HEAD")
                    {
                        byte[] bb = Encoding.UTF8.GetBytes(bodyText);
                        req.ContentType = "application/json; charset=utf-8";
                        req.ContentLength = bb.Length;
                        using (Stream s = req.GetRequestStream()) s.Write(bb, 0, bb.Length);
                    }

                    HttpWebResponse resp = null;
                    try
                    {
                        resp = (HttpWebResponse)req.GetResponse();
                    }
                    catch (WebException wex)
                    {
                        resp = wex.Response as HttpWebResponse;
                        if (resp == null) throw;
                    }

                    if (resp != null)
                    {
                        status = (int)resp.StatusCode;
                        if (!string.IsNullOrEmpty(resp.ContentType)) ctype = resp.ContentType;
                        using (Stream rs = resp.GetResponseStream())
                        {
                            MemoryStream ms = new MemoryStream();
                            byte[] buf = new byte[16384];
                            int n;
                            while ((n = rs.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                            data = ms.ToArray();
                        }
                        resp.Close();
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("proxy fail " + target + " : " + ex.Message);
                    data = Encoding.UTF8.GetBytes("{\"error\":" +
                        JsonQuote(ex.Message) + "}");
                    ctype = "application/json; charset=utf-8";
                }

                this.CompleteProxy(e, deferral, data, status, ctype);
            });
        }

        // ================= 多书源引擎（Python sidecar: nd_bridge.exe） =================

        private sealed class MultiJob
        {
            public string id;
            public string site;
            public string bookId;
            public string title;
            public string state;     // queued / running / done / failed
            public string message;
            public string file;
            public int done;
            public int total;
            public DateTime started;
        }

        private readonly List<MultiJob> multiJobs = new List<MultiJob>();

        private static string ResolveNdExe()
        {
            string env = Environment.GetEnvironmentVariable("TOMATO_ND_EXE");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nd", "nd_bridge.exe");
            if (File.Exists(p)) return p;
            return null;
        }

        private static string Q(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return "";
            string[] parts = query.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = parts[i].Substring(0, eq);
                if (string.Compare(k, key, true) != 0) continue;
                string v = parts[i].Substring(eq + 1);
                try { v = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
                return v;
            }
            return "";
        }

        private static string QuoteArg(string a)
        {
            if (a == null) return "\"\"";
            if (a.Length == 0) return "\"\"";
            if (a.IndexOf(' ') >= 0 || a.IndexOf('"') >= 0)
            {
                return "\"" + a.Replace("\"", "") + "\"";
            }
            return a;
        }

        private static string RunNd(string[] args, int timeoutMs)
        {
            string exe = ResolveNdExe();
            if (exe == null)
            {
                return "{\"ok\":false,\"error\":\"多书源引擎未安装（缺少 nd\\\\nd_bridge.exe）\"}";
            }
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            StringBuilder ab = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) ab.Append(' ');
                ab.Append(QuoteArg(args[i]));
            }
            psi.Arguments = ab.ToString();
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Path.GetDirectoryName(exe);

            try
            {
                using (Process p = Process.Start(psi))
                {
                    StringBuilder outp = new StringBuilder();
                    StringBuilder errp = new StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs ev)
                    {
                        if (ev.Data != null) outp.Append(ev.Data).Append('\n');
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs ev)
                    {
                        if (ev.Data != null) errp.Append(ev.Data).Append('\n');
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return "{\"ok\":false,\"error\":\"多书源引擎响应超时\"}";
                    }
                    p.WaitForExit();

                    string text = outp.ToString();
                    string[] lines = text.Split('\n');
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string ln = lines[i].Trim();
                        if (ln.Length > 1 && ln[0] == '{') return ln;
                    }
                    string e2 = errp.ToString().Trim();
                    if (e2.Length > 600) e2 = e2.Substring(e2.Length - 600);
                    if (e2.Length == 0) e2 = "引擎没有返回数据";
                    return "{\"ok\":false,\"error\":" + JsonQuote(e2) + "}";
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + JsonQuote("启动引擎失败: " + ex.Message) + "}";
            }
        }

        private string GetSaveDir()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(this.baseUrl.TrimEnd('/') + "/api/status");
                req.Proxy = null;
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string js = sr.ReadToEnd();
                    string sd = JsonField(js, "save_dir");
                    if (!string.IsNullOrEmpty(sd))
                    {
                        sd = sd.Replace("\\\\", "\\");
                        if (sd.Length > 0) return sd;
                    }
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "大肥鱼小说");
        }

        private void HandleMulti(CoreWebView2WebResourceRequestedEventArgs e, string pathOnly, string mq)
        {
            CoreWebView2Deferral d = e.GetDeferral();
            Task.Run(delegate
            {
                string json = "{\"ok\":false,\"error\":\"unknown\"}";
                int st = 200;
                try
                {
                    if (pathOnly == "/api/multi/sites")
                    {
                        json = RunNd(new string[] { "sites" }, 60000);
                    }
                    else if (pathOnly == "/api/multi/search")
                    {
                        string kw = Q(mq, "q");
                        string sites = Q(mq, "sites");
                        if (kw.Length == 0)
                        {
                            json = "{\"ok\":false,\"error\":\"缺少关键词\"}";
                        }
                        else
                        {
                            string[] args = sites.Length > 0
                                ? new string[] { "search", kw, "--sites", sites, "--per-site", "5" }
                                : new string[] { "search", kw, "--per-site", "5" };
                            json = RunNd(args, 120000);
                        }
                    }
                    else if (pathOnly == "/api/multi/info")
                    {
                        json = RunNd(new string[] { "info", Q(mq, "site"), Q(mq, "id") }, 120000);
                    }
                    else if (pathOnly == "/api/multi/download")
                    {
                        json = this.StartMultiDownload(Q(mq, "site"), Q(mq, "id"), Q(mq, "format"), Q(mq, "title"));
                    }
                    else if (pathOnly == "/api/multi/jobs")
                    {
                        json = this.MultiJobsJson();
                    }
                    else
                    {
                        st = 404;
                        json = "{\"ok\":false,\"error\":\"not found\"}";
                    }
                }
                catch (Exception ex)
                {
                    json = "{\"ok\":false,\"error\":" + JsonQuote(ex.Message) + "}";
                }
                this.CompleteProxy(e, d, Encoding.UTF8.GetBytes(json), st, "application/json; charset=utf-8");
            });
        }

        private string StartMultiDownload(string site, string bookId, string fmt, string title)
        {
            if (string.IsNullOrEmpty(site) || string.IsNullOrEmpty(bookId))
            {
                return "{\"ok\":false,\"error\":\"缺少站点或书籍 ID\"}";
            }
            if (ResolveNdExe() == null)
            {
                return "{\"ok\":false,\"error\":\"多书源引擎未安装\"}";
            }
            if (fmt == null || fmt.Length == 0) fmt = "txt";

            MultiJob job = new MultiJob();
            job.id = Guid.NewGuid().ToString("N").Substring(0, 12);
            job.site = site;
            job.bookId = bookId;
            job.title = string.IsNullOrEmpty(title) ? (site + "/" + bookId) : title;
            job.state = "running";
            job.message = "准备下载";
            job.done = 0;
            job.total = 0;
            job.started = DateTime.Now;

            lock (this.multiJobs)
            {
                this.multiJobs.Add(job);
                while (this.multiJobs.Count > 40) this.multiJobs.RemoveAt(0);
            }

            string outDir = this.GetSaveDir();
            string[] args = new string[] { "download", site, bookId, outDir, "--format", fmt, "--workers", "4" };
            Program.Log("multi download -> " + site + "/" + bookId + "  out=" + outDir);

            Task.Run(delegate
            {
                string exe = ResolveNdExe();
                if (exe == null) { job.state = "failed"; job.message = "引擎缺失"; return; }
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exe);
                    StringBuilder ab = new StringBuilder();
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (i > 0) ab.Append(' ');
                        ab.Append(QuoteArg(args[i]));
                    }
                    psi.Arguments = ab.ToString();
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                    psi.WorkingDirectory = Path.GetDirectoryName(exe);

                    using (Process p = Process.Start(psi))
                    {
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            string t = line.Trim();
                            if (t.Length < 2 || t[0] != '{') continue;
                            string ev = JsonField(t, "ev");
                            if (ev == "progress")
                            {
                                string dn = JsonField(t, "done");
                                string tt = JsonField(t, "total");
                                int a, b;
                                if (int.TryParse(dn, out a)) job.done = a;
                                if (int.TryParse(tt, out b)) job.total = b;
                                job.message = "下载中";
                            }
                            else if (ev == "start")
                            {
                                job.message = "获取目录";
                            }
                            else if (ev == "complete")
                            {
                                job.message = "正在导出";
                            }
                            else if (t.IndexOf("\"files\"") >= 0)
                            {
                                job.file = JsonField(t, "path");
                            }
                            else if (t.IndexOf("\"error\"") >= 0)
                            {
                                job.state = "failed";
                                job.message = JsonField(t, "error");
                            }
                        }
                        p.WaitForExit();
                        if (job.state != "failed")
                        {
                            job.state = "done";
                            job.message = "完成";
                        }
                    }
                }
                catch (Exception ex)
                {
                    job.state = "failed";
                    job.message = ex.Message;
                }
                Program.Log("multi job " + job.id + " -> " + job.state + " " + job.message);
            });

            return "{\"ok\":true,\"job\":" + JsonQuote(job.id) + "}";
        }

        private string MultiJobsJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"items\":[");
            lock (this.multiJobs)
            {
                bool first = true;
                for (int i = this.multiJobs.Count - 1; i >= 0; i--)
                {
                    MultiJob j = this.multiJobs[i];
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('{');
                    sb.Append("\"id\":").Append(JsonQuote(j.id)).Append(',');
                    sb.Append("\"site\":").Append(JsonQuote(j.site)).Append(',');
                    sb.Append("\"book_id\":").Append(JsonQuote(j.bookId)).Append(',');
                    sb.Append("\"title\":").Append(JsonQuote(j.title)).Append(',');
                    sb.Append("\"state\":").Append(JsonQuote(j.state)).Append(',');
                    sb.Append("\"message\":").Append(JsonQuote(j.message)).Append(',');
                    sb.Append("\"file\":").Append(JsonQuote(j.file)).Append(',');
                    sb.Append("\"done\":").Append(j.done).Append(',');
                    sb.Append("\"total\":").Append(j.total).Append(',');
                    sb.Append("\"started\":").Append(JsonQuote(j.started.ToString("HH:mm:ss")));
                    sb.Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string MimeOf(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".ico": return "image/x-icon";
                case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }

        // 从 ui 目录提供静态文件
        private void ServeStatic(CoreWebView2WebResourceRequestedEventArgs e, string pathOnly)
        {
            CoreWebView2Deferral deferral = e.GetDeferral();
            byte[] data;
            int status = 200;
            string ctype;
            try
            {
                string name = pathOnly.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string rootFull = Path.GetFullPath(this.uiDir);
                string full = Path.GetFullPath(Path.Combine(rootFull, name));
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                {
                    data = Encoding.UTF8.GetBytes("404 not found: " + pathOnly);
                    status = 404;
                    ctype = "text/plain; charset=utf-8";
                }
                else
                {
                    data = File.ReadAllBytes(full);
                    ctype = MimeOf(full);
                }
            }
            catch (Exception ex)
            {
                Program.Log("serve static fail: " + ex.Message);
                data = Encoding.UTF8.GetBytes("500 " + ex.Message);
                status = 500;
                ctype = "text/plain; charset=utf-8";
            }
            this.CompleteProxy(e, deferral, data, status, ctype);
        }

        private static string JsonQuote(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder("\"");
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private void CompleteProxy(CoreWebView2WebResourceRequestedEventArgs e,
            CoreWebView2Deferral deferral, byte[] data, int status, string ctype)
        {
            try
            {
                this.BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        MemoryStream ms = new MemoryStream(data == null ? new byte[0] : data);
                        string headers = "Content-Type: " + ctype + "\r\nCache-Control: no-store\r\n";
                        e.Response = this.web.CoreWebView2.Environment
                            .CreateWebResourceResponse(ms, status, ReasonText(status), headers);
                    }
                    catch (Exception ex) { Program.Log("proxy response fail: " + ex.Message); }
                    finally { try { deferral.Complete(); } catch { } }
                });
            }
            catch (Exception ex)
            {
                Program.Log("proxy marshal fail: " + ex.Message);
                try { deferral.Complete(); } catch { }
            }
        }

        private static string ReasonText(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 201: return "Created";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 409: return "Conflict";
                case 500: return "Internal Server Error";
                case 502: return "Bad Gateway";
                default: return "Status";
            }
        }

        // ---- 自研前端：处理页面发来的消息（打开外部链接 / 刷新） ----
        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json = null;
            try { json = e.WebMessageAsJson; } catch { }
            if (string.IsNullOrEmpty(json)) return;

            string type = JsonField(json, "type");
            string url = JsonField(json, "url");
            Program.Log("webmessage: " + type + " " + (url == null ? "" : url));

            if (type == "openUrl" && !string.IsNullOrEmpty(url))
            {
                if (url.StartsWith("/")) url = this.baseUrl.TrimEnd('/') + url;
                try { Process.Start(url); }
                catch (Exception ex) { Program.Log("openUrl fail: " + ex.Message); }
            }
            else if (type == "reload")
            {
                try { this.web.CoreWebView2.Reload(); } catch { }
            }
        }

        private static string JsonField(string json, string name)
        {
            if (json == null) return null;
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            int c = json.IndexOf(':', i + key.Length);
            if (c < 0) return null;
            int p = c + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length) return null;

            if (json[p] == '"')
            {
                StringBuilder sb = new StringBuilder();
                p++;
                while (p < json.Length && json[p] != '"')
                {
                    if (json[p] == '\\' && p + 1 < json.Length)
                    {
                        p++;
                        char esc = json[p];
                        if (esc == 'n') sb.Append('\n');
                        else if (esc == 'r') sb.Append('\r');
                        else if (esc == 't') sb.Append('\t');
                        else if (esc == 'u' && p + 4 < json.Length)
                        {
                            int code;
                            if (int.TryParse(json.Substring(p + 1, 4),
                                System.Globalization.NumberStyles.HexNumber, null, out code))
                            {
                                sb.Append((char)code);
                                p += 4;
                            }
                        }
                        else sb.Append(esc);
                    }
                    else sb.Append(json[p]);
                    p++;
                }
                return sb.ToString();
            }

            int e2 = p;
            while (e2 < json.Length && json[e2] != ',' && json[e2] != '}') e2++;
            return json.Substring(p, e2 - p).Trim();
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            try { Process.Start(e.Uri); } catch { }
        }

        private void OnBackendLine(string line)
        {
            if (line == null) return;
            lock (this.backendOut)
            {
                this.backendOut.AppendLine(line);
                if (this.backendOut.Length > 20000)
                    this.backendOut.Remove(0, this.backendOut.Length - 16000);
            }
            Program.Log("backend| " + line);
        }

        private string BackendTail()
        {
            string s;
            lock (this.backendOut) { s = this.backendOut.ToString(); }
            if (s.Length > 700) s = s.Substring(s.Length - 700);
            return s.Trim();
        }

        private bool StartServerAndWait(string exe, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, "--server");
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                this.server = Process.Start(psi);
                this.ownServer = true;
                this.server.OutputDataReceived += delegate(object s, DataReceivedEventArgs a)
                { this.OnBackendLine(a.Data); };
                this.server.ErrorDataReceived += delegate(object s, DataReceivedEventArgs a)
                { this.OnBackendLine(a.Data); };
                this.server.BeginOutputReadLine();
                this.server.BeginErrorReadLine();
                Program.Log("后端 PID = " + this.server.Id);
            }
            catch (Exception ex)
            {
                Program.Log("spawn fail: " + ex.Message);
                return false;
            }
            return this.WaitHttp(timeoutMs);
        }

        private bool WaitHttp(int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (this.server != null)
                {
                    try { if (this.server.HasExited) return false; }
                    catch { }
                }
                if (this.IsHttpReady()) return true;
                Thread.Sleep(350);
            }
            return this.IsHttpReady();
        }

        private bool IsHttpReady()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(this.baseUrl);
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;
                req.Proxy = null;
                req.AllowAutoRedirect = false;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    return code >= 200 && code < 500;
                }
            }
            catch (WebException wex)
            {
                HttpWebResponse r = wex.Response as HttpWebResponse;
                if (r != null)
                {
                    int c = (int)r.StatusCode;
                    r.Close();
                    return c >= 200 && c < 500;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Program.Log("窗口关闭，清理后端");
            this.StopServer();
            base.OnFormClosing(e);
        }

        private void StopServer()
        {
            if (!this.ownServer || this.server == null) return;
            try
            {
                if (!this.server.HasExited)
                {
                    ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/F /T /PID " + this.server.Id);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
            }
            catch { }
            try { if (!this.server.HasExited) this.server.Kill(); } catch { }
            this.server = null;
            this.ownServer = false;
        }
    }
}
