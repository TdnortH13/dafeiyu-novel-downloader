/* ===== 番茄小说下载器 · 自定义前端 ===== */
'use strict';

// 收集前端运行时错误（便于宿主自检日志定位）
window.__errs = [];
window.addEventListener('error', function (e) {
  try { window.__errs.push(String(e.message || e.error)); } catch (x) { }
});
window.addEventListener('unhandledrejection', function (e) {
  try {
    var r = e.reason;
    window.__errs.push('reject: ' + (r && r.message ? r.message : String(r)));
  } catch (x) { }
});

const $ = (s, r) => (r || document).querySelector(s);
const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
const esc = (v) => String(v == null ? '' : v).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

/* ---------- 基础工具 ---------- */
function fmtBytes(n) {
  n = Number(n || 0);
  if (n < 1024) return n + ' B';
  if (n < 1048576) return (n / 1024).toFixed(1) + ' KB';
  if (n < 1073741824) return (n / 1048576).toFixed(1) + ' MB';
  return (n / 1073741824).toFixed(2) + ' GB';
}
function fmtTime(ms) {
  if (!ms) return '—';
  const d = new Date(Number(ms));
  if (isNaN(d.getTime())) return '—';
  const p = (x) => String(x).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
function toast(msg, kind) {
  const box = $('#toasts');
  const el = document.createElement('div');
  el.className = 'toast ' + (kind || '');
  el.textContent = msg;
  box.appendChild(el);
  setTimeout(() => {
    el.style.opacity = '0';
    el.style.transform = 'translateY(6px)';
    el.style.transition = '.25s';
    setTimeout(() => el.remove(), 260);
  }, 3200);
}

async function api(path, opts) {
  const res = await fetch(path, Object.assign({ credentials: 'same-origin' }, opts || {}));
  if (res.status === 401) {
    const ok = await askPassword();
    if (ok) return api(path, opts);
    throw new Error('未授权');
  }
  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch (e) { data = text; }
  if (!res.ok) {
    const m = (data && (data.error || data.message)) || text || ('HTTP ' + res.status);
    throw new Error(String(m).slice(0, 300));
  }
  return data;
}
function post(path, body, method) {
  return api(path, {
    method: method || 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body || {})
  });
}

/* ---------- 登录（密码锁模式） ---------- */
let loginResolver = null;
function askPassword() {
  return new Promise((resolve) => {
    loginResolver = resolve;
    openModal(`
      <h2 style="margin:0 0 6px;font-size:18px">需要密码</h2>
      <p class="hint" style="margin-bottom:16px">该 Web UI 已开启锁模式，请输入密码。</p>
      <input class="input" id="pwInput" type="password" placeholder="密码" autofocus>
      <div style="display:flex;gap:10px;justify-content:flex-end;margin-top:18px">
        <button class="btn primary" id="pwOk">确定</button>
      </div>`);
    const submit = async () => {
      const pw = ($('#pwInput') || {}).value || '';
      try {
        await post('/api/login', { password: pw });
        closeModal();
        const r = loginResolver; loginResolver = null; if (r) r(true);
      } catch (e) { toast('密码错误', 'err'); }
    };
    $('#pwOk').onclick = submit;
    $('#pwInput').onkeydown = (e) => { if (e.key === 'Enter') submit(); };
    setTimeout(() => { const i = $('#pwInput'); if (i) i.focus(); }, 60);
  });
}

/* ---------- 主题 ---------- */
const THEME_KEY = 'tnd.theme';
function applyTheme(t) {
  document.documentElement.setAttribute('data-theme', t);
  try { localStorage.setItem(THEME_KEY, t); } catch (e) { }
}
function initTheme() {
  let t = null;
  try { t = localStorage.getItem(THEME_KEY); } catch (e) { }
  if (!t) t = window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', t);
}
$('#themeBtn').onclick = () => {
  const cur = document.documentElement.getAttribute('data-theme');
  applyTheme(cur === 'dark' ? 'light' : 'dark');
};

/* ---------- 弹窗 ---------- */
function openModal(html) {
  $('#modalBody').innerHTML = html;
  $('#modal').hidden = false;
}
function closeModal() {
  $('#modal').hidden = true;
  $('#modalBody').innerHTML = '';
}
$('#modalClose').onclick = closeModal;
$('#modal').addEventListener('click', (e) => { if (e.target.id === 'modal') closeModal(); });
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeModal(); });

/* ---------- 路由 ---------- */
const PAGES = {
  status: { title: '状态', sub: '程序版本、运行环境与更新情况' },
  search: { title: '搜索下载', sub: '按书名或作者搜索，选择章节范围后加入下载队列' },
  jobs: { title: '任务', sub: '下载队列与实时进度' },
  history: { title: '下载历史', sub: '所有已完成的下载记录' },
  updates: { title: '检查更新', sub: '扫描本地书籍，检查是否有新增章节' },
  library: { title: '下载库', sub: '浏览已下载的文件' },
  config: { title: '配置', sub: '所有下载与输出参数' }
};
let currentPage = 'status';

function route() {
  const h = (location.hash || '#status').replace('#', '');
  const page = PAGES[h] ? h : 'status';
  currentPage = page;
  $$('.nav-item').forEach(a => a.classList.toggle('on', a.dataset.page === page));
  $$('.view').forEach(v => v.hidden = (v.id !== 'view-' + page));
  $('#pageTitle').textContent = PAGES[page].title;
  $('#pageSub').textContent = PAGES[page].sub;
  $('#headActions').innerHTML = '';
  if (page === 'status') loadStatus();
  if (page === 'search') setTimeout(() => { const i = $('#searchInput'); if (i) i.focus(); }, 50);
  if (page === 'jobs') loadJobs();
  if (page === 'history') loadHistory();
  if (page === 'updates') loadUpdates(false);
  if (page === 'library') loadLibrary(false);
  if (page === 'config') loadConfig();
}
window.addEventListener('hashchange', route);

/* ---------- 状态 ---------- */
let lastStatus = null;

async function loadStatus() {
  try {
    const s = await api('/api/status');
    lastStatus = s;
    $('#verLabel').textContent = 'v' + (s.version || '—');
    const prewarm = s.prewarm_in_progress ? '预热中…' : (s.prewarm_error ? '预热失败' : '就绪');
    const cards = [
      { k: '程序版本', v: 'v' + (s.version || '—'), sub: s.docker_build ? 'Docker 构建' : '桌面版' },
      { k: '监听地址', v: (s.bind_addrs || [s.bind_addr]).join('  ·  '), mono: true, sub: s.locked ? '已开启密码锁' : '未设密码' },
      { k: '保存目录', v: s.save_dir || '—', mono: true, sub: (s.config && s.config.save_path) ? ('自定义：' + s.config.save_path) : '使用默认目录' },
      { k: '接口状态', v: prewarm, sub: (s.config && s.config.use_official_api) ? '官方 API 直连' : ('第三方 API × ' + ((s.config && s.config.api_endpoints_len) || 0)) }
    ];
    $('#statGrid').innerHTML = cards.map(c => `
      <div class="stat">
        <div class="k">${esc(c.k)}</div>
        <div class="v${c.mono ? ' mono' : ''}">${esc(c.v)}</div>
        ${c.sub ? `<div class="sub">${esc(c.sub)}</div>` : ''}
      </div>`).join('');
  } catch (e) {
    $('#statGrid').innerHTML = `<div class="stat"><div class="k">连接失败</div><div class="v">${esc(e.message)}</div></div>`;
  }
}

$('#btnCheckApp').onclick = async (ev) => {
  const btn = ev.target; btn.disabled = true; btn.textContent = '检查中…';
  try {
    const d = await api('/api/app_update');
    const box = $('#appUpdateBox');
    if (!d.has_update) {
      box.innerHTML = `<div class="empty-line">已是最新版本 <span class="badge success">v${esc(d.current)}</span></div>`;
    } else {
      box.innerHTML = `
        <div style="display:flex;align-items:center;gap:10px;margin-bottom:10px">
          <span class="badge accent">发现新版本</span>
          <strong>${esc(d.latest_tag || '')}</strong>
          <span class="hint" style="margin:0">${esc(d.latest_name || '')}</span>
        </div>
        <div class="hint" style="white-space:pre-wrap;line-height:1.8">${esc((d.latest_body || '').slice(0, 1200))}</div>
        <div style="display:flex;gap:10px;margin-top:14px">
          <button class="btn primary sm" id="btnSelfUpdate">立即更新</button>
          <button class="btn ghost sm" id="btnOpenRelease">打开发布页</button>
        </div>`;
      $('#btnOpenRelease').onclick = () => openExternal(d.latest_url || '');
      $('#btnSelfUpdate').onclick = async () => {
        toast('已开始自更新，程序会自动重启…', 'warn');
        try { await post('/api/self_update'); } catch (e) { toast('更新失败：' + e.message, 'err'); }
      };
    }
  } catch (e) { toast('检查失败：' + e.message, 'err'); }
  btn.disabled = false; btn.textContent = '检查更新';
};

function openExternal(url) {
  if (!url) return;
  try {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ type: 'openUrl', url: url });
    } else {
      window.open(url, '_blank');
    }
  } catch (e) { }
}

/* ---------- 搜索 ---------- */
$('#searchForm').addEventListener('submit', (e) => {
  e.preventDefault();
  const q = ($('#searchInput').value || '').trim();
  if (q) doSearch(q);
});

/* ---------- 多书源 ---------- */
const MULTI_DEFAULT_SITES = 'biquge345,fsshu,bxwx9,blqudu,shuhaige,yibige,b520,n37yue,i25zw,ixdzs8,dushu,quanben5,ruochu,shaoniandream,lewenn,lvsewx,mjyhb,haiwaishubao,shauthor,n17k,qqbook,ciyuanji,sfacg,yamibo,akatsuki_novels,biquge5,n37yq,mangg_com,novelpia';

const SITE_NAMES = {
  fanqienovel: '番茄', biquge345: '笔趣阁345', fsshu: '笔趣阁fsshu', bxwx9: '笔下文学',
  blqudu: '笔趣阁blqudu', shuhaige: '书海阁', yibige: '易笔趣阁', b520: '笔趣阁520',
  n37yue: '笔趣阁37', i25zw: '爱阅中文', ixdzs8: '笔下文学8', dushu: '读书网',
  quanben5: '全本小说', ruochu: '若初文学', shaoniandream: '少年梦', lewenn: '乐文',
  lvsewx: '绿色文学', mjyhb: '墨家', haiwaishubao: '海外书包', shauthor: '书作者',
  n17k: '17K', qqbook: 'QQ阅读', ciyuanji: '次元姬', sfacg: 'SF轻小说', yamibo: '百合会',
  akatsuki_novels: '晓', biquge5: '笔趣阁5', n37yq: '笔趣阁37yq', mangg_com: '芒果',
  novelpia: 'Novelpia', shencou: '神凑', zhenhunxiaoshuo: '镇魂小说', n71ge: '71阁',
  ciweimao: '刺猬猫', linovelib: '哔哩轻小说', biquguo: '笔趣阁国'
};

function siteName(s) { return SITE_NAMES[s] || s; }

async function loadSources() {
  const sel = $('#srcSel');
  if (!sel) return;
  try {
    const d = await api('/api/multi/sites');
    if (!d || !d.sites) return;
    sel.innerHTML = '<option value="">全部常用站</option>' +
      d.sites.map(s => `<option value="${esc(s)}">${esc(siteName(s))}</option>`).join('');
  } catch (e) { }
}

async function doSearch(q) {
  const box = $('#searchResults');
  const wantTomato = $('#srcTomato') ? $('#srcTomato').checked : true;
  const wantMulti = $('#srcMulti') ? $('#srcMulti').checked : false;
  const one = $('#srcSel') ? $('#srcSel').value : '';
  const sites = one || MULTI_DEFAULT_SITES;

  $('#searchHint').textContent = '搜索中…';
  box.innerHTML = '<div class="empty"><span class="loading"></span></div>';

  const tasks = [];
  if (wantTomato) {
    tasks.push(api('/api/search?q=' + encodeURIComponent(q))
      .then(d => (d.items || []).map(x => ({ src: 'fanqienovel', title: x.title, author: x.author, book_id: x.book_id })))
      .catch(() => []));
  }
  if (wantMulti) {
    tasks.push(api('/api/multi/search?q=' + encodeURIComponent(q) + '&sites=' + encodeURIComponent(sites))
      .then(d => (d.items || []).map(x => ({ src: x.site, title: x.title, author: x.author, book_id: x.book_id })))
      .catch(() => []));
  }
  if (!tasks.length) {
    box.innerHTML = '<div class="empty"><b>没有选择任何书源</b>至少勾选「番茄官方」或「多书源」</div>';
    $('#searchHint').textContent = '';
    return;
  }

  const groups = await Promise.all(tasks);
  const items = [].concat.apply([], groups);

  if (!items.length) {
    $('#searchHint').textContent = '';
    box.innerHTML = '<div class="empty"><b>没有找到结果</b>换个书名或作者试试</div>';
    return;
  }

  const tomatoCount = items.filter(x => x.src === 'fanqienovel').length;
  $('#searchHint').textContent = `共 ${items.length} 条结果（番茄 ${tomatoCount} 条 · 其他站 ${items.length - tomatoCount} 条）`;

  box.innerHTML = items.map(x => `
    <div class="result">
      <div class="r-main">
        <div class="r-title">${esc(x.title || '未知书名')}</div>
        <div class="r-meta">
          <span class="badge">${esc(siteName(x.src))}</span>
          <span>${esc(x.author || '未知作者')}</span>
          <code>${esc(x.book_id)}</code>
        </div>
      </div>
      <button class="btn primary sm" data-bid="${esc(x.book_id)}" data-site="${esc(x.src)}">下载</button>
    </div>`).join('');

  $$('button[data-bid]', box).forEach(btn => {
    btn.onclick = () => {
      if (btn.dataset.site === 'fanqienovel') openPreview(btn.dataset.bid);
      else openMultiPreview(btn.dataset.site, btn.dataset.bid);
    };
  });
}

async function openMultiPreview(site, bookId) {
  openModal('<div class="empty"><span class="loading"></span> 正在获取书籍信息…</div>');
  let d;
  try {
    d = await api('/api/multi/info?site=' + encodeURIComponent(site) + '&id=' + encodeURIComponent(bookId));
  } catch (e) {
    openModal(`<h2 style="margin:0 0 8px;font-size:17px">获取失败</h2><p class="hint">${esc(e.message)}</p>
      <div style="text-align:right;margin-top:16px"><button class="btn ghost sm" id="pvClose3">关闭</button></div>`);
    $('#pvClose3').onclick = closeModal;
    return;
  }
  if (!d || d.ok === false) {
    const msg = (d && d.error) || '未知错误';
    openModal(`<h2 style="margin:0 0 8px;font-size:17px">获取失败</h2><p class="hint">${esc(msg)}</p>
      <div style="text-align:right;margin-top:16px"><button class="btn ghost sm" id="pvClose4">关闭</button></div>`);
    $('#pvClose4').onclick = closeModal;
    return;
  }

  const cover = d.cover_url
    ? `<img class="pv-cover" src="${esc(d.cover_url)}" alt="" onerror="this.style.visibility='hidden'">`
    : '';

  openModal(`
    <div class="pv">
      ${cover}
      <div class="pv-info">
        <h2>${esc(d.title || '未知书名')}</h2>
        <div class="pv-author">${esc(d.author || '未知作者')}</div>
        <div class="pv-stats">
          <span class="badge accent">${esc(siteName(site))}</span>
          <span class="badge info">${d.chapter_count || 0} 章</span>
        </div>
        ${d.intro ? `<div class="pv-desc">${esc(d.intro)}</div>` : ''}
        <div class="pv-actions">
          <button class="btn primary" id="pvDl">开始下载</button>
        </div>
        <div class="hint" id="pvMsg"></div>
      </div>
    </div>`);

  $('#pvDl').onclick = async () => {
    const btn = $('#pvDl');
    btn.disabled = true;
    btn.textContent = '正在加入…';
    try {
      const url = '/api/multi/download?site=' + encodeURIComponent(site) +
        '&id=' + encodeURIComponent(bookId) +
        '&format=txt&title=' + encodeURIComponent(d.title || '');
      const r = await api(url);
      if (r && r.ok) {
        toast('已加入下载队列', 'ok');
        closeModal();
        location.hash = '#jobs';
        pollMultiJobs();
      } else {
        btn.disabled = false;
        btn.textContent = '重试';
        $('#pvMsg').textContent = (r && r.error) || '加入失败';
      }
    } catch (e) {
      btn.disabled = false;
      btn.textContent = '重试';
      $('#pvMsg').textContent = e.message;
    }
  };
}

async function pollMultiJobs() {
  const box = $('#multiJobList');
  if (!box) return;
  let d;
  try { d = await api('/api/multi/jobs'); } catch (e) { return; }
  if (!d || !d.items) return;
  if (!d.items.length) {
    box.innerHTML = '<div class="empty-line">暂无多书源任务</div>';
    return;
  }
  const running = d.items.filter(j => j.state === 'running').length;
  const hint = $('#mJobsHint');
  if (hint) hint.textContent = running ? running + ' 个进行中' : '';

  box.innerHTML = d.items.map(j => {
    const pct = j.total > 0 ? Math.round(j.done * 100 / j.total) : 0;
    const cls = j.state === 'done' ? 'success' : (j.state === 'failed' ? 'danger' : 'accent');
    const st = j.state === 'done' ? 'done' : (j.state === 'failed' ? 'failed' : 'running');
    return `
      <div class="job state-${st}">
        <div class="j-top">
          <span class="j-title">${esc(j.title)}</span>
          <span class="badge ${cls}">${esc(siteName(j.site))} · ${j.state === 'running' ? pct + '%' : (j.state === 'done' ? '完成' : '失败')}</span>
        </div>
        <div class="j-bar"><i style="width:${pct}%"></i></div>
        <div class="j-foot">
          <span>${j.total > 0 ? j.done + ' / ' + j.total + ' 章' : esc(j.message || '准备中')}${j.file ? ' · ' + esc(String(j.file).split('\\').pop()) : ''}</span>
          <span>${esc(j.started || '')}</span>
        </div>
      </div>`;
  }).join('');
}

loadSources();
setInterval(pollMultiJobs, 3000);


/* ---------- 预览 ---------- */
function coverCandidates(p) {
  const list = [];
  ['thumb_url', 'cover_url', 'image_url', 'audio_thumb_uri', 'thumb_uri', 'expand_thumb'].forEach(k => {
    if (p && p[k]) list.push(p[k]);
  });
  return list.filter(u => typeof u === 'string' && u.length > 8);
}

async function openPreview(bookId) {
  openModal('<div class="empty"><span class="loading"></span> 正在获取书籍信息…</div>');
  let p;
  try {
    p = await api('/api/preview/' + encodeURIComponent(bookId));
  } catch (e) {
    openModal(`<h2 style="margin:0 0 8px;font-size:17px">获取失败</h2><p class="hint">${esc(e.message)}</p>
      <div style="text-align:right;margin-top:16px"><button class="btn ghost sm" id="pvClose2">关闭</button></div>`);
    $('#pvClose2').onclick = closeModal;
    return;
  }

  const stats = [];
  if (p.chapter_count) stats.push(`共 ${p.chapter_count} 章`);
  if (p.finished !== null && p.finished !== undefined) stats.push(p.finished ? '已完结' : '连载中');
  if (p.word_count) {
    const w = Number(p.word_count);
    stats.push(w >= 10000 ? (w / 10000).toFixed(1) + ' 万字' : w + ' 字');
  }
  if (p.score != null) stats.push('评分 ' + Number(p.score).toFixed(1));
  if (p.read_count_text || p.read_count) stats.push('阅读 ' + (p.read_count_text || p.read_count));

  const chapterInfo = [];
  if (p.first_chapter_title) chapterInfo.push('首章：' + p.first_chapter_title);
  if (p.last_chapter_title) chapterInfo.push('末章：' + p.last_chapter_title);
  if (p.category) chapterInfo.push('分类：' + p.category);

  const covers = coverCandidates(p);
  openModal(`
    <div class="pv">
      <img class="pv-cover" id="pvCover" ${covers.length ? '' : 'hidden'} src="${esc(covers[0] || '')}" alt="">
      <div class="pv-info">
        <h2>${esc(p.book_name || '未知书名')}</h2>
        ${p.original_book_name && p.original_book_name !== p.book_name ? `<div class="pv-orig">原名：${esc(p.original_book_name)}</div>` : ''}
        <div class="pv-author">作者：${esc(p.author || '未知')}</div>
        <div class="pv-stats">${stats.map(s => `<span class="badge info">${esc(s)}</span>`).join('')}</div>
        ${(p.tags && p.tags.length) ? `<div class="pv-stats">${p.tags.slice(0, 8).map(t => `<span class="badge">${esc(t)}</span>`).join('')}</div>` : ''}
        <div class="pv-desc">${esc(p.description || '暂无简介')}</div>
        ${chapterInfo.length ? `<div class="pv-chapters">${chapterInfo.map(c => `<div>${esc(c)}</div>`).join('')}</div>` : ''}
      </div>
    </div>
    <div class="pv-actions">
      <span class="hint" style="margin:0">章节范围</span>
      <input class="input" id="pvRange" placeholder="全部" style="width:120px">
      <span class="hint" style="margin:0">如 1-20，留空下载全部</span>
      <span class="spacer"></span>
      <button class="btn primary" id="pvStart">开始下载</button>
    </div>`);

  const cover = $('#pvCover');
  if (cover && covers.length > 1) {
    let i = 1;
    cover.onerror = () => {
      if (i < covers.length) cover.src = covers[i++];
      else cover.hidden = true;
    };
  }
  $('#pvRange').onkeydown = (e) => { if (e.key === 'Enter') $('#pvStart').click(); };
  $('#pvStart').onclick = () => startDownload(p.book_id || bookId);
}

async function startDownload(bookId) {
  const raw = (($('#pvRange') || {}).value || '').trim();
  const payload = { book_id: bookId };
  if (raw) {
    const m = raw.match(/^(\d+)\s*[-~]\s*(\d+)$/);
    if (!m) { toast('范围格式应为 1-20', 'err'); return; }
    let a = parseInt(m[1], 10), b = parseInt(m[2], 10);
    if (a > b) { const t = a; a = b; b = t; }
    payload.range_start = a; payload.range_end = b;
  }
  try {
    await post('/api/jobs', payload);
    closeModal();
    toast('已加入下载队列', 'ok');
    location.hash = '#jobs';
  } catch (e) {
    toast('创建任务失败：' + e.message, 'err');
  }
}

/* ---------- 任务 ---------- */
let jobsTimer = null;

function jobStateLabel(st, hasCfg) {
  if (hasCfg) return { cls: 'warning', text: '待配置' };
  switch (st) {
    case 'running': return { cls: 'info', text: '下载中' };
    case 'queued': return { cls: '', text: '排队中' };
    case 'done': return { cls: 'success', text: '完成' };
    case 'failed': return { cls: 'danger', text: '失败' };
    case 'partial': return { cls: 'warning', text: '部分失败' };
    case 'canceled': return { cls: '', text: '已取消' };
    default: return { cls: '', text: st || '未知' };
  }
}

async function loadJobs(silent) {
  try {
    const d = await api('/api/jobs');
    const items = d.items || [];
    $('#jobsDot').hidden = !items.some(it => it.state === 'running' || it.state === 'queued');
    if (currentPage !== 'jobs') return;

    const box = $('#jobList');
    if (!items.length) {
      box.innerHTML = '<div class="empty"><b>暂无任务</b>去「搜索下载」找一本书吧</div>';
      $('#jobsHint').textContent = '';
      return;
    }
    const active = items.filter(it => it.state === 'running' || it.state === 'queued').length;
    $('#jobsHint').textContent = `${items.length} 个任务 · ${active} 个进行中`;

    box.innerHTML = items.map(it => {
      const pr = it.progress || {};
      const saved = Number(pr.saved_chapters || 0);
      const total = Number(pr.chapter_total || 0);
      const pct = total > 0 ? Math.min(100, Math.round(saved / total * 100)) : 0;
      let st = (it.state || '').toLowerCase();
      if (st === 'done' && total > 0 && saved < total) st = 'partial';
      const hasCfg = (it.book_name_options || []).length > 0 || (it.format_options || []).length > 0;
      const lab = jobStateLabel(st, hasCfg);
      const title = it.title || it.book_id || '未知';

      let actions = '';
      if (hasCfg) {
        const kind = (it.book_name_options || []).length ? 'book_name' : 'format';
        actions = `<button class="btn sm ghost" data-act="cfg" data-id="${esc(it.id)}" data-kind="${kind}">去选择</button>`;
      } else if (st === 'running' || st === 'queued') {
        actions = `<button class="btn sm danger" data-act="cancel" data-id="${esc(it.id)}">取消</button>`;
      } else if (st === 'failed' || st === 'partial' || st === 'canceled') {
        actions = `<button class="btn sm ghost" data-act="retry" data-bid="${esc(it.book_id)}">重试</button>
                   <button class="btn sm ghost" data-act="del" data-id="${esc(it.id)}">移除</button>`;
      } else {
        actions = `<button class="btn sm ghost" data-act="del" data-id="${esc(it.id)}">移除</button>`;
      }

      return `
        <div class="job state-${esc(st)}">
          <div class="j-top">
            <span class="j-title">${esc(title)}</span>
            <span class="badge ${lab.cls}">${lab.text}${st === 'running' ? ' ' + pct + '%' : ''}</span>
          </div>
          <div class="j-bar"><i style="width:${pct}%"></i></div>
          <div class="j-foot">
            <span>${total > 0 ? `${saved} / ${total} 章` : '等待开始'}${it.message ? ' · ' + esc(String(it.message).slice(0, 90)) : ''}</span>
            <span style="display:flex;gap:8px">${actions}</span>
          </div>
        </div>`;
    }).join('');

    $$('[data-act]', box).forEach(btn => {
      const act = btn.dataset.act;
      btn.onclick = async () => {
        try {
          if (act === 'cancel') { await post('/api/jobs/' + encodeURIComponent(btn.dataset.id) + '/cancel'); toast('已取消', 'ok'); }
          else if (act === 'del') { await api('/api/jobs/' + encodeURIComponent(btn.dataset.id), { method: 'DELETE' }); toast('已移除', 'ok'); }
          else if (act === 'retry') { await post('/api/jobs', { book_id: btn.dataset.bid }); toast('已重新加入队列', 'ok'); }
          else if (act === 'cfg') { await promptJobConfig(btn.dataset.id, btn.dataset.kind); }
          loadJobs();
        } catch (e) { toast('操作失败：' + e.message, 'err'); }
      };
    });
  } catch (e) {
    if (!silent && currentPage === 'jobs') {
      $('#jobList').innerHTML = `<div class="empty"><b>读取任务失败</b>${esc(e.message)}</div>`;
    }
  }
}

async function promptJobConfig(jobId, kind) {
  const d = await api('/api/jobs?id=' + encodeURIComponent(jobId));
  const it = (d.items || [])[0] || {};
  const opts = kind === 'book_name' ? (it.book_name_options || []) : (it.format_options || []);
  if (!opts.length) { toast('没有可选项', 'warn'); return; }
  const isName = kind === 'book_name';
  openModal(`
    <h2 style="margin:0 0 6px;font-size:17px">${isName ? '选择书名' : '选择输出格式'}</h2>
    <p class="hint" style="margin-bottom:14px">下载完成后需要确认一项设置才能继续。</p>
    <div style="display:flex;flex-direction:column;gap:8px">
      ${opts.map((o, i) => {
        const val = typeof o === 'string' ? o : (o.value || o.name || '');
        const label = typeof o === 'string' ? o : (o.label || o.value || o.name || '');
        return `<button class="btn ghost" data-opt="${esc(val)}" style="text-align:left;justify-content:flex-start">${esc(label)}</button>`;
      }).join('')}
    </div>`);
  $$('[data-opt]').forEach(b => {
    b.onclick = async () => {
      try {
        await post('/api/jobs/' + encodeURIComponent(jobId) + '/' + kind, isName ? { name: b.dataset.opt } : { format: b.dataset.opt });
        closeModal(); toast('已提交', 'ok'); loadJobs();
      } catch (e) { toast('提交失败：' + e.message, 'err'); }
    };
  });
}

/* ---------- 历史 ---------- */
async function loadHistory() {
  const kw = (($('#histKw') || {}).value || '').trim();
  const body = $('#histBody');
  body.innerHTML = '<tr><td colspan="6"><span class="loading"></span></td></tr>';
  const qs = new URLSearchParams({ limit: '200' });
  if (kw) qs.set('q', kw);
  try {
    const d = await api('/api/history?' + qs.toString());
    const items = d.items || [];
    $('#histHint').textContent = items.length ? `共 ${items.length} 条` : '';
    if (!items.length) {
      body.innerHTML = '<tr><td colspan="6"><div class="empty">暂无下载记录</div></td></tr>';
      return;
    }
    body.innerHTML = items.map(it => {
      const ok = String(it.status || '').toLowerCase() === 'success';
      return `<tr>
        <td>${esc(it.timestamp || '')}</td>
        <td>${esc(it.book_name || '')}</td>
        <td>${esc(it.author || '')}</td>
        <td><code>${esc(it.book_id || '')}</code></td>
        <td>${esc(it.progress || '')}</td>
        <td><span class="badge ${ok ? 'success' : 'danger'}">${ok ? '成功' : '失败'}</span></td>
      </tr>`;
    }).join('');
  } catch (e) {
    body.innerHTML = `<tr><td colspan="6"><div class="empty">读取失败：${esc(e.message)}</div></td></tr>`;
  }
}
$('#btnHist').onclick = loadHistory;
$('#histKw').addEventListener('keydown', e => { if (e.key === 'Enter') loadHistory(); });

/* ---------- 更新扫描 ---------- */
let updTimer = null;

async function loadUpdates(start) {
  const body = $('#updBody');
  if (start) {
    $('#updHint').innerHTML = '<span class="loading"></span> 扫描中…';
    body.innerHTML = '';
  }
  try {
    const d = await api(start ? '/api/updates' : '/api/updates?start=false');
    const ups = d.updates || [];
    const no = d.no_updates || [];
    const total = ups.length + no.length;
    if (d.running) {
      const p = d.total ? `${d.scanned}/${d.total}` : String(d.scanned || 0);
      $('#updHint').innerHTML = `<span class="loading"></span> 扫描中 ${p} · 可更新 ${ups.length} 本`;
      clearTimeout(updTimer);
      updTimer = setTimeout(() => loadUpdates(false), 1200);
    } else {
      $('#updHint').textContent = `可更新 ${ups.length} 本 / 无更新 ${no.length} 本 / 共 ${total} 本`;
    }

    const rows = [];
    for (const u of ups) {
      rows.push(`<tr>
        <td>${esc(u.book_name || u.title || u.book_id || '')}</td>
        <td><span class="badge accent">有更新</span></td>
        <td>${esc(u.local_chapters != null ? u.local_chapters : (u.local_count != null ? u.local_count : '—'))}</td>
        <td>${esc(u.remote_chapters != null ? u.remote_chapters : (u.remote_count != null ? u.remote_count : '—'))}</td>
        <td><button class="btn sm primary" data-upd="${esc(u.book_id || '')}">更新</button></td>
      </tr>`);
    }
    for (const u of no) {
      rows.push(`<tr>
        <td>${esc(u.book_name || u.title || u.book_id || '')}</td>
        <td><span class="badge">已最新</span></td>
        <td>${esc(u.local_chapters != null ? u.local_chapters : (u.local_count != null ? u.local_count : '—'))}</td>
        <td>${esc(u.remote_chapters != null ? u.remote_chapters : (u.remote_count != null ? u.remote_count : '—'))}</td>
        <td>—</td>
      </tr>`);
    }
    body.innerHTML = rows.length ? rows.join('') : '<tr><td colspan="5"><div class="empty">没有扫描到本地书籍</div></td></tr>';
    $$('[data-upd]', body).forEach(b => {
      b.onclick = async () => {
        try { await post('/api/jobs', { book_id: b.dataset.upd }); toast('已加入更新队列', 'ok'); }
        catch (e) { toast('失败：' + e.message, 'err'); }
      };
    });
  } catch (e) {
    $('#updHint').textContent = '扫描失败：' + e.message;
  }
}
$('#btnScan').onclick = () => loadUpdates(true);

/* ---------- 下载库 ---------- */
let libPath = '';

async function loadLibrary(start) {
  const body = $('#libBody');
  if (start) body.innerHTML = '<tr><td colspan="4"><span class="loading"></span></td></tr>';
  try {
    const qs = new URLSearchParams();
    if (libPath) qs.set('path', libPath);
    qs.set('start', start ? 'true' : 'false');
    const d = await api('/api/library?' + qs.toString());
    libPath = (d.path || '').toString();
    $('#libPath').textContent = libPath ? '/' + libPath : '/';
    $('#libBack').disabled = !libPath;
    const items = d.items || [];
    $('#libHint').textContent = d.error ? ('读取失败：' + d.error)
      : (d.running ? `分批读取中，已发现 ${items.length} 项` : `共 ${items.length} 项`);

    if (!items.length) {
      body.innerHTML = `<tr><td colspan="4"><div class="empty"><b>${d.running ? '正在读取…' : '这里还是空的'}</b>${d.running ? '' : '先去下载一本书吧'}</div></td></tr>`;
    } else {
      body.innerHTML = items.map(it => {
        const kind = it.kind || 'file';
        const rel = it.rel_path || '';
        const name = it.name || rel;
        const size = kind === 'dir'
          ? (it.file_count == null ? '文件夹' : `${fmtBytes(it.size)} · ${it.file_count} 个文件`)
          : fmtBytes(it.size);
        const act = kind === 'dir'
          ? `<button class="btn sm ghost" data-dir="${esc(rel)}">打开</button>
             <button class="btn sm ghost" data-zip="${esc(rel)}">打包下载</button>`
          : `<button class="btn sm ghost" data-file="${esc(rel)}">下载</button>`;
        return `<tr>
          <td>${kind === 'dir' ? '📁 ' : '📄 '}${esc(name)}${it.ext ? ` <span class="badge">${esc(it.ext)}</span>` : ''}</td>
          <td>${esc(size)}</td>
          <td>${esc(fmtTime(it.modified_ms))}</td>
          <td>${act}</td>
        </tr>`;
      }).join('');
      $$('[data-dir]', body).forEach(b => b.onclick = () => { libPath = b.dataset.dir; loadLibrary(true); });
      $$('[data-file]', body).forEach(b => b.onclick = () => openExternal('/download/' + encodePath(b.dataset.file)));
      $$('[data-zip]', body).forEach(b => b.onclick = () => openExternal('/download-zip/' + encodePath(b.dataset.zip)));
    }
    if (d.running) setTimeout(() => loadLibrary(false), 800);
  } catch (e) {
    $('#libHint').textContent = '读取失败：' + e.message;
  }
}
function encodePath(p) {
  return String(p).split('/').map(encodeURIComponent).join('/');
}
$('#libBack').onclick = () => {
  if (!libPath) return;
  const parts = libPath.split('/').filter(Boolean);
  parts.pop();
  libPath = parts.join('/');
  loadLibrary(true);
};
$('#libRefresh').onclick = () => loadLibrary(true);

/* ---------- 配置 ---------- */
const CFG_SCHEMA = [
  {
    title: '基础与输出', fields: [
      { key: 'save_path', label: '保存路径', type: 'text', placeholder: '留空使用程序默认目录', wide: true },
      {
        key: 'novel_format', label: '小说格式', type: 'select', options: [
          { value: 'txt', label: 'txt 纯文本' },
          { value: 'epub', label: 'epub 电子书' },
          { value: 'pdf', label: 'pdf 文档' },
          { value: 'bulk_txt', label: '散装 txt（按章节分文件）' },
          { value: 'ask_after_download', label: '下载完成后再询问' }
        ]
      },
      {
        key: 'preferred_book_name_field', label: '优先书名', type: 'select', options: [
          { value: 'book_name', label: '默认书名' },
          { value: 'original_book_name', label: '原始书名' },
          { value: 'book_short_name', label: '短书名' },
          { value: 'ask_after_download', label: '下载完成后再询问' }
        ]
      },
      { key: 'first_line_indent_em', label: '首行缩进 (em)', type: 'number', step: '0.1' },
      { key: 'pdf_font_path', label: 'PDF 字体路径', type: 'text', placeholder: '留空使用系统字体' },
      { key: 'auto_clear_dump', label: '自动清理缓存', type: 'bool' },
      { key: 'auto_open_downloaded_files', label: '完成后自动打开文件', type: 'bool' },
      { key: 'allow_overwrite_files', label: '允许覆盖已有文件', type: 'bool' },
      { key: 'old_cli', label: '使用旧版命令行界面', type: 'bool' }
    ]
  },
  {
    title: '网络与调度', fields: [
      { key: 'max_workers', label: '最大线程数', type: 'number', min: 1 },
      { key: 'request_timeout', label: '请求超时 (秒)', type: 'number', min: 1 },
      { key: 'max_retries', label: '最大重试次数', type: 'number', min: 0 },
      { key: 'min_connect_timeout', label: '最小连接超时 (秒)', type: 'number', step: '0.05' },
      { key: 'min_wait_time', label: '最小等待 (ms)', type: 'number', min: 0 },
      { key: 'max_wait_time', label: '最大等待 (ms)', type: 'number', min: 0 }
    ]
  },
  {
    title: '接口', fields: [
      { key: 'use_official_api', label: '使用官方 API', type: 'bool' },
      { key: 'api_endpoints', label: '第三方 API 列表', type: 'list', placeholder: '每行一个，或用逗号分隔', wide: true }
    ]
  },
  {
    title: '段评', fields: [
      { key: 'enable_segment_comments', label: '启用段评', type: 'bool' },
      { key: 'segment_comments_top_n', label: '每段评论数上限', type: 'number', min: 1 },
      { key: 'segment_comments_workers', label: '段评并发线程数', type: 'number', min: 1 }
    ]
  },
  {
    title: '图片与媒体', fields: [
      { key: 'download_comment_images', label: '下载评论图片', type: 'bool' },
      { key: 'download_comment_avatars', label: '下载评论头像', type: 'bool' },
      { key: 'media_download_workers', label: '媒体下载线程数', type: 'number', min: 1 },
      { key: 'media_limit_per_chapter', label: '单章节媒体上限', type: 'number', min: 0 },
      { key: 'media_max_dimension_px', label: '媒体最大尺寸 (px)', type: 'number', min: 0 },
      { key: 'force_convert_images_to_jpeg', label: '强制转成 JPEG', type: 'bool' },
      { key: 'jpeg_retry_convert', label: '失败后重试转 JPEG', type: 'bool' },
      { key: 'jpeg_quality', label: 'JPEG 质量 (0-100)', type: 'number', min: 0, max: 100 },
      { key: 'convert_heic_to_jpeg', label: 'HEIC 转 JPEG', type: 'bool' },
      { key: 'keep_heic_original', label: '保留 HEIC 原图', type: 'bool' },
      { key: 'blocked_media_domains', label: '屏蔽的图片域名', type: 'list', placeholder: '每行一个域名', wide: true }
    ]
  },
  {
    title: '有声书', fields: [
      { key: 'enable_audiobook', label: '启用有声书', type: 'bool' },
      { key: 'audiobook_voice', label: '发音人', type: 'text', placeholder: 'zh-CN-XiaoxiaoNeural' },
      { key: 'audiobook_format', label: '输出格式', type: 'select', options: [{ value: 'mp3', label: 'mp3' }, { value: 'wav', label: 'wav' }] },
      {
        key: 'audiobook_tts_provider', label: 'TTS 服务', type: 'select', options: [
          { value: 'edge', label: 'edge（免费）' }, { value: 'third_party', label: '第三方 API' }
        ]
      },
      { key: 'audiobook_rate', label: '语速调整', type: 'text', placeholder: '+0%' },
      { key: 'audiobook_volume', label: '音量调整', type: 'text', placeholder: '+0%' },
      { key: 'audiobook_pitch', label: '音调调整', type: 'text', placeholder: '+0Hz' },
      { key: 'audiobook_concurrency', label: '并发生成章节数', type: 'number', min: 1 },
      { key: 'audiobook_tts_api_url', label: '第三方 TTS 地址', type: 'text' },
      { key: 'audiobook_tts_api_token', label: '第三方 TTS Token', type: 'text' },
      { key: 'audiobook_tts_model', label: '第三方 TTS 模型', type: 'text' }
    ]
  }
];

let fullCfg = {};
let cfgTab = 'form';

async function loadConfig() {
  if (cfgTab === 'raw') return loadRaw();
  $('#cfgMsg').textContent = '加载中…';
  try {
    fullCfg = await api('/api/config/full') || {};
    renderCfgForm(fullCfg);
    $('#cfgMsg').textContent = '';
  } catch (e) {
    $('#cfgMsg').textContent = '加载失败：' + e.message;
  }
}

function renderCfgForm(cfg) {
  const box = $('#cfgForm');
  box.innerHTML = CFG_SCHEMA.map(sec => `
    <div class="cfg-group">
      <h3>${esc(sec.title)}</h3>
      <div class="cfg-fields">
        ${sec.fields.map(f => fieldHtml(f, cfg)).join('')}
      </div>
    </div>`).join('');
}

function fieldHtml(f, cfg) {
  const val = cfg[f.key];
  const wide = f.wide ? ' wide' : '';
  let inner = '';
  if (f.type === 'bool') {
    return `<div class="field${wide}">
      <label class="switch">
        <input type="checkbox" data-key="${esc(f.key)}" data-type="bool" ${val ? 'checked' : ''}>
        <span class="track"></span><span class="sw-text">${esc(f.label)}</span>
      </label>
    </div>`;
  }
  if (f.type === 'select') {
    inner = `<select class="input" data-key="${esc(f.key)}" data-type="select">
      ${(f.options || []).map(o => `<option value="${esc(o.value)}" ${String(val) === String(o.value) ? 'selected' : ''}>${esc(o.label)}</option>`).join('')}
    </select>`;
  } else if (f.type === 'list') {
    const text = Array.isArray(val) ? val.join('\n') : '';
    inner = `<textarea data-key="${esc(f.key)}" data-type="list" rows="3" placeholder="${esc(f.placeholder || '')}">${esc(text)}</textarea>`;
  } else if (f.type === 'number') {
    inner = `<input class="input" type="number" data-key="${esc(f.key)}" data-type="number"
      ${f.step ? `step="${esc(f.step)}"` : ''} ${f.min != null ? `min="${f.min}"` : ''} ${f.max != null ? `max="${f.max}"` : ''}
      value="${esc(val == null ? '' : val)}">`;
  } else {
    inner = `<input class="input" type="text" data-key="${esc(f.key)}" data-type="text"
      value="${esc(val == null ? '' : val)}" placeholder="${esc(f.placeholder || '')}">`;
  }
  return `<div class="field${wide}"><label>${esc(f.label)} <code>${esc(f.key)}</code></label>${inner}</div>`;
}

function collectCfg() {
  const out = Object.assign({}, fullCfg);
  $$('[data-key]', $('#cfgForm')).forEach(el => {
    const k = el.dataset.key, t = el.dataset.type;
    if (t === 'bool') out[k] = !!el.checked;
    else if (t === 'list') out[k] = (el.value || '').split(/[\n,;]/).map(s => s.trim()).filter(Boolean);
    else if (t === 'number') {
      const raw = (el.value || '').trim();
      if (raw === '') { out[k] = null; return; }
      const n = Number(raw);
      out[k] = isNaN(n) ? null : n;
    } else out[k] = (el.value || '').toString();
  });
  if (out.novel_format === 'ask_after_download') {
    out.ask_format_after_download = true; out.bulk_files = false;
    out.novel_format = (fullCfg.novel_format === 'ask_after_download' ? 'txt' : (fullCfg.novel_format || 'txt'));
  } else if (out.novel_format === 'bulk_txt') {
    out.ask_format_after_download = false; out.bulk_files = true; out.novel_format = 'txt';
  } else {
    out.ask_format_after_download = false; out.bulk_files = false;
  }
  if (out.pdf_font_path === '') out.pdf_font_path = null;
  return out;
}

async function loadRaw() {
  $('#cfgMsg').textContent = '加载中…';
  try {
    const d = await api('/api/config/raw');
    $('#rawYaml').value = (d.yaml || '').toString();
    $('#cfgMsg').textContent = d.generated ? '当前显示的是默认配置（未找到配置文件）' : '';
  } catch (e) { $('#cfgMsg').textContent = '加载失败：' + e.message; }
}

$('#btnSaveCfg').onclick = async (ev) => {
  const btn = ev.target; btn.disabled = true; btn.textContent = '保存中…';
  try {
    if (cfgTab === 'raw') {
      await post('/api/config/raw', { yaml: $('#rawYaml').value || '' });
      toast('YAML 已保存', 'ok');
    } else {
      await post('/api/config/full', collectCfg());
      toast('配置已保存', 'ok');
      loadStatus();
    }
    $('#cfgMsg').textContent = '已保存 ' + new Date().toLocaleTimeString();
  } catch (e) {
    toast('保存失败：' + e.message, 'err');
  }
  btn.disabled = false; btn.textContent = '保存';
};

$$('#cfgTabs button').forEach(b => {
  b.onclick = () => {
    cfgTab = b.dataset.tab;
    $$('#cfgTabs button').forEach(x => x.classList.toggle('on', x === b));
    $('#cfgForm').hidden = cfgTab !== 'form';
    $('#cfgRaw').hidden = cfgTab !== 'raw';
    if (cfgTab === 'raw') loadRaw(); else loadConfig();
  };
});

/* ---------- 启动 ---------- */
initTheme();
route();
loadStatus();
loadJobs(true);
setInterval(() => { if (currentPage === 'jobs') loadJobs(true); else loadJobs(true); }, 2000);
setInterval(() => { if (currentPage === 'status') loadStatus(); }, 8000);
