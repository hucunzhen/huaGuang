using System.Net;
using System.Text;
using System.Text.Json;
using HuaGuang.Monitor.Models;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services;

internal sealed class SubscribeWebReadModel
{
    /// <summary>参数为详情设备 Id；空则只返回机台汇总（不含全量点位）。</summary>
    public required Func<string?, SubscribeWebStatus> Status { get; init; }
    public required Func<AppSettings> Settings { get; init; }
    public Func<AppSettings>? ReloadSettings { get; init; }
    public required HistoryStore History { get; init; }
    public required ILogger Logger { get; init; }
}

/// <summary>订阅网页的只读路由。没有启停和改点。</summary>
internal static class SubscribeWebHttp
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static SubscribeWebReadModel CreateReadModel(
        Func<string?, SubscribeWebStatus> status,
        Func<AppSettings> settings,
        HistoryStore history) =>
        new()
        {
            Status = status,
            Settings = settings,
            ReloadSettings = settings,
            History = history,
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        };

    public static async Task HandleAsync(
        HttpListenerContext context,
        SubscribeWebReadModel model,
        SemaphoreSlim exportGate,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var path = (request.Url?.AbsolutePath ?? "/").TrimEnd('/');
            if (path.Length == 0)
            {
                path = "/";
            }

            var method = request.HttpMethod ?? "GET";
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                && string.Equals(path, "/api/login", StringComparison.OrdinalIgnoreCase))
            {
                await LoginAsync(context, model).ConfigureAwait(false);
                return;
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                && string.Equals(path, "/api/logout", StringComparison.OrdinalIgnoreCase))
            {
                Logout(context, request);
                return;
            }

            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                WriteText(context, 405, "text/plain; charset=utf-8", "只读页面，不接受修改。");
                return;
            }

            var session = SubscribeWebSessions.ReadCookie(request.Headers["Cookie"]);
            var signedIn = SubscribeWebSessions.IsValid(session);
            if (signedIn)
            {
                SubscribeWebSessions.Touch(session);
            }
            if (path is "/" or "/index.html")
            {
                WriteText(context, 200, "text/html; charset=utf-8", signedIn ? PageHtml : LoginPage(null));
                return;
            }

            if (!signedIn)
            {
                WriteText(context, 401, "text/plain; charset=utf-8", "请先登录。");
                return;
            }

            if (string.Equals(path, "/api/status", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var detailDevice = ReadQueryValue(request, "device");
                    var json = JsonSerializer.Serialize(model.Status(detailDevice), JsonOptions);
                    WriteText(context, 200, "application/json; charset=utf-8", json);
                }
                catch (Exception ex)
                {
                    model.Logger.LogWarning(ex, "订阅网页 /api/status 生成失败");
                    WriteText(context, 500, "text/plain; charset=utf-8", "状态生成失败，请稍后重试。");
                }

                return;
            }

            if (string.Equals(path, "/api/history/export", StringComparison.OrdinalIgnoreCase))
            {
                await ExportAsync(context, model, exportGate, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/api/history", StringComparison.OrdinalIgnoreCase))
            {
                await QueryAsync(context, model).ConfigureAwait(false);
                return;
            }

            WriteText(context, 404, "text/plain; charset=utf-8", "未找到页面。");
        }
        catch (Exception ex)
        {
            model.Logger.LogDebug(ex, "订阅网页响应失败");
            try
            {
                context.Response.Abort();
            }
            catch
            {
            }
        }
    }

    static async Task LoginAsync(HttpListenerContext context, SubscribeWebReadModel model)
    {
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        if (body.Length > 4096)
        {
            WriteText(context, 400, "text/plain; charset=utf-8", "登录内容过长。");
            return;
        }

        var form = SubscribeWebSessions.ParseForm(body);
        form.TryGetValue("username", out var username);
        form.TryGetValue("password", out var password);
        var settings = model.ReloadSettings?.Invoke() ?? model.Settings();
        if (!WebLoginAccount.Matches(settings, username, password))
        {
            WriteText(context, 200, "text/html; charset=utf-8", LoginPage("用户名或密码不正确。"));
            return;
        }

        var token = SubscribeWebSessions.Issue();
        var response = context.Response;
        response.StatusCode = 302;
        response.RedirectLocation = "/";
        response.Headers["Set-Cookie"] =
            $"{SubscribeWebSessions.CookieName}={token}; HttpOnly; Path=/; SameSite=Lax; Max-Age={SubscribeWebSessions.Lifetime.TotalSeconds:0}";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = 0;
        response.OutputStream.Close();
    }

    static void Logout(HttpListenerContext context, HttpListenerRequest request)
    {
        SubscribeWebSessions.Revoke(SubscribeWebSessions.ReadCookie(request.Headers["Cookie"]));
        var response = context.Response;
        response.StatusCode = 302;
        response.RedirectLocation = "/";
        response.Headers["Set-Cookie"] = $"{SubscribeWebSessions.CookieName}=; HttpOnly; Path=/; SameSite=Lax; Max-Age=0";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = 0;
        response.OutputStream.Close();
    }

    static string LoginPage(string? error)
    {
        var message = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"<div class=\"banner\">{error}</div>";
        return LoginHtml.Replace("<!--error-->", message, StringComparison.Ordinal);
    }

    static async Task QueryAsync(HttpListenerContext context, SubscribeWebReadModel model)
    {
        if (!SubscribeWebHistory.TryParse(context.Request.Url, DateTimeOffset.Now, out var filter, out var error))
        {
            WriteText(context, 400, "text/plain; charset=utf-8", error);
            return;
        }

        try
        {
            var page = await SubscribeWebHistory.QueryAsync(model.History, model.Settings(), filter).ConfigureAwait(false);
            WriteText(context, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(page, JsonOptions));
        }
        catch (InvalidOperationException ex)
        {
            WriteText(context, 400, "text/plain; charset=utf-8", ex.Message);
        }
        catch (Exception ex)
        {
            model.Logger.LogWarning(ex, "订阅网页查询历史失败");
            WriteText(context, 500, "text/plain; charset=utf-8", "查询失败。");
        }
    }

    static async Task ExportAsync(
        HttpListenerContext context,
        SubscribeWebReadModel model,
        SemaphoreSlim exportGate,
        CancellationToken cancellationToken)
    {
        if (!SubscribeWebHistory.TryParse(context.Request.Url, DateTimeOffset.Now, out var filter, out var error))
        {
            WriteText(context, 400, "text/plain; charset=utf-8", error);
            return;
        }

        if (!await exportGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            WriteText(context, 429, "text/plain; charset=utf-8", "正在导出，请稍后再试。");
            return;
        }

        SubscribeWebExportFile? file = null;
        try
        {
            file = await SubscribeWebHistory.ExportAsync(model.History, model.Settings(), filter, cancellationToken)
                .ConfigureAwait(false);
            model.Logger.LogInformation(
                "订阅网页导出 device={Device} rows={Rows} file={File}",
                filter.DeviceId ?? "全部设备",
                file.RowCount,
                file.DownloadName);
            WriteFile(context, file);
        }
        catch (InvalidOperationException ex)
        {
            WriteText(context, 400, "text/plain; charset=utf-8", ex.Message);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            model.Logger.LogWarning(ex, "订阅网页导出失败");
            WriteText(context, 500, "text/plain; charset=utf-8", "导出失败。");
        }
        finally
        {
            file?.Dispose();
            exportGate.Release();
        }
    }

    static void WriteFile(HttpListenerContext context, SubscribeWebExportFile file)
    {
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Content-Disposition"] = ContentDisposition(file.DownloadName);
        using var stream = File.OpenRead(file.Path);
        response.ContentLength64 = stream.Length;
        stream.CopyTo(response.OutputStream);
        response.OutputStream.Close();
    }

    static string ContentDisposition(string fileName)
    {
        var ascii = new string(fileName.Select(ch => ch <= 127 ? ch : '_').ToArray());
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }

    /// <summary>自解析 query，避免 HttpListener 对中文 device 参数解码不一致。</summary>
    static string? ReadQueryValue(HttpListenerRequest request, string name)
    {
        var query = request.Url?.Query;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? part : part[..eq]).Replace('+', ' '));
            if (!key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    static void WriteText(HttpListenerContext context, int statusCode, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var response = context.Response;
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    const string LoginHtml = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>订阅大屏登录</title>
<style>
  body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center; font-family: "Segoe UI", sans-serif; background: #0e1a27; color: #d7e2ec; }
  form { width: min(360px, calc(100% - 32px)); background: #152536; border-radius: 10px; padding: 20px; display: flex; flex-direction: column; gap: 12px; }
  h1 { margin: 0; font-size: 20px; }
  label { display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: #8aa0b5; }
  input, button { background: #0e1a27; color: #d7e2ec; border: 1px solid #1f3a52; border-radius: 6px; padding: 8px; font: inherit; }
  button { cursor: pointer; background: #1d4e89; }
  .banner { color: #ff6b6b; font-size: 13px; }
</style>
</head>
<body>
<form method="post" action="/api/login">
  <h1>订阅大屏</h1>
  <label>用户名<input name="username" autocomplete="username" required /></label>
  <label>密码<input name="password" type="password" autocomplete="current-password" required /></label>
  <button type="submit">登录</button>
  <!--error-->
</form>
</body>
</html>
""";

    const string PageHtml = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>订阅大屏</title>
<style>
  :root { color-scheme: dark; }
  body { margin: 0; font-family: "Segoe UI", sans-serif; background: #0e1a27; color: #d7e2ec; }
  header { padding: 16px 20px 8px; display: flex; justify-content: space-between; gap: 12px; align-items: center; }
  h1 { margin: 0 0 8px; font-size: 20px; font-weight: 600; }
  .meta { color: #8aa0b5; font-size: 13px; }
  .banner { margin: 0 20px 12px; color: #ffb347; font-size: 13px; min-height: 1em; }
  nav button { margin-right: 6px; }
  button.on { background: #1d4e89; border-color: #1d4e89; }
  .toolbar { display: flex; flex-wrap: wrap; gap: 10px; align-items: flex-end; padding: 0 16px 12px; }
  label { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: #8aa0b5; }
  input, select, button { background: #152536; color: #d7e2ec; border: 1px solid #1f3a52; border-radius: 6px; padding: 6px 8px; font: inherit; }
  button { cursor: pointer; }
  button:disabled { opacity: 0.5; cursor: default; }
  .hint { padding: 0 16px 8px; color: #8aa0b5; font-size: 12px; }
  main { padding: 0 16px 24px; display: flex; flex-direction: column; gap: 16px; }
  .device-head { background: #101C28; border: 1px solid #1F3A52; border-radius: 8px; padding: 10px 12px; display: flex; justify-content: space-between; gap: 12px; align-items: flex-start; }
  .device-head h2 { margin: 0; font-size: 16px; color: #fff; }
  .topic { color: #8aa0b5; font-size: 12px; margin-top: 2px; word-break: break-all; }
  .fleet { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
  .fleet-card { border: 1px solid #1F3A52; border-radius: 8px; background: #152536; padding: 12px 14px; cursor: pointer; display: grid; grid-template-columns: 4px minmax(0, 1fr); gap: 12px; min-height: 118px; }
  .fleet-card:hover { border-color: #2EC4B6; }
  .fleet-card.stale { border-color: #5A2A2A; }
  .fleet-rail { border-radius: 2px; }
  .fleet-top { display: flex; justify-content: space-between; gap: 8px; align-items: flex-start; }
  .fleet-id { color: #fff; font-weight: 700; font-size: 16px; }
  .fleet-run { font-weight: 700; font-size: 13px; white-space: nowrap; }
  .fleet-line { color: #8aa0b5; font-size: 12px; margin-top: 2px; }
  .fleet-fresh { display: flex; align-items: center; gap: 6px; font-weight: 700; margin-top: 8px; }
  .fleet-status { color: #C9D6E2; font-size: 12px; margin-top: 6px; }
  .fleet-detail { color: #6E8499; font-size: 12px; margin-top: 4px; word-break: break-all; }
  .fleet-switches { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
  .fleet-chip { display: inline-flex; align-items: center; gap: 6px; border: 1px solid; border-radius: 6px; padding: 4px 8px; font-size: 12px; max-width: 100%; }
  .fleet-chip .chip-dot { width: 8px; height: 8px; border-radius: 4px; flex: none; }
  .fleet-chip .chip-name { color: #8AA0B5; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 7em; }
  .fleet-chip .chip-value { font-weight: 700; white-space: nowrap; }
  .back-btn { white-space: nowrap; }
  .group-head { display: flex; align-items: center; gap: 8px; margin: 14px 0 6px; }
  .group-bar { width: 4px; height: 18px; border-radius: 2px; }
  .group-title { font-weight: 700; font-size: 15px; }
  .group-count { color: #8aa0b5; font-size: 13px; }
  .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(210px, 1fr)); gap: 8px; }
  .point { border-radius: 8px; box-sizing: border-box; min-height: 78px; }
  .point.switch { display: flex; gap: 12px; align-items: center; padding: 10px 12px; border-width: 2px; border-style: solid; }
  .badge { width: 52px; height: 52px; border-radius: 26px; display: flex; align-items: center; justify-content: center; color: #fff; font-weight: 700; font-size: 16px; flex: none; }
  .point-body { min-width: 0; }
  .point .name { color: #C9D6E2; font-size: 14px; }
  .point .status { font-weight: 700; font-size: 16px; margin-top: 2px; }
  .point.meter { display: grid; grid-template-columns: 4px minmax(0, 1fr); border-width: 1px; border-style: solid; }
  .meter-bar { border-radius: 2px 0 0 2px; }
  .meter-body { padding: 8px 10px; min-width: 0; }
  .reading { display: flex; gap: 4px; align-items: flex-end; margin-top: 2px; }
  .reading b { font-size: 18px; font-variant-numeric: tabular-nums; }
  .unit { color: #8AA0B5; font-size: 12px; margin-bottom: 2px; }
  .addr { color: #6E8499; font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .foot { display: flex; gap: 6px; align-items: center; color: #8AA0B5; font-size: 12px; margin-top: 4px; }
  .dot { width: 7px; height: 7px; border-radius: 4px; flex: none; }
  .empty { padding: 8px 20px 24px; color: #8aa0b5; }
  .table-wrap { overflow: auto; max-height: calc(100vh - 240px); margin: 0 16px; }
  table { border-collapse: collapse; min-width: 100%; }
  th, td { padding: 6px 10px; border-bottom: 1px solid #1f3a52; white-space: nowrap; font-size: 13px; text-align: left; }
  th { position: sticky; top: 0; background: #152536; }
  .pager { display: flex; gap: 8px; align-items: center; padding: 12px 16px 24px; }
</style>
</head>
<body>
<header>
  <div>
    <h1>订阅大屏</h1>
    <nav>
      <button type="button" id="tab-live" class="on">汇总</button>
      <button type="button" id="tab-history">历史</button>
      <button type="button" id="logout">退出</button>
    </nav>
  </div>
  <div class="meta" id="meta">正在读取…</div>
</header>
<div class="banner" id="banner"></div>
<section id="live">
  <div class="toolbar" id="live-toolbar" hidden>
    <button type="button" id="live-back" class="back-btn">← 返回汇总</button>
    <label>设备<select id="live-device"></select></label>
    <label>点位<input id="live-tag" placeholder="名称包含" /></label>
  </div>
  <div class="hint" id="fleet-hint">数采机台状态。点击进入该设备点位数据。</div>
  <main id="board"></main>
</section>
<section id="history" hidden>
  <div class="toolbar">
    <label>设备<select id="hist-device"></select></label>
    <label>开始<input id="hist-from" type="datetime-local" /></label>
    <label>结束<input id="hist-to" type="datetime-local" /></label>
    <label>点位<input id="hist-tag" placeholder="名称包含" /></label>
    <button type="button" id="hist-query">查询</button>
    <button type="button" id="hist-export">导出</button>
  </div>
  <div class="hint">单次最多 31 天。导出当前筛选结果，最多 20000 条。只读，不能删除。</div>
  <div class="meta" id="hist-summary"></div>
  <div class="empty" id="hist-empty" hidden></div>
  <div class="table-wrap" id="hist-wrap" hidden><table id="hist-table"></table></div>
  <div class="pager">
    <button type="button" id="hist-prev">上一页</button>
    <span id="hist-page"></span>
    <button type="button" id="hist-next">下一页</button>
  </div>
</section>
<script>
const meta = document.getElementById("meta");
const banner = document.getElementById("banner");
const board = document.getElementById("board");
const liveDevice = document.getElementById("live-device");
const liveTag = document.getElementById("live-tag");
const histDevice = document.getElementById("hist-device");
const histFrom = document.getElementById("hist-from");
const histTo = document.getElementById("hist-to");
const histTag = document.getElementById("hist-tag");
const histSummary = document.getElementById("hist-summary");
const histEmpty = document.getElementById("hist-empty");
const histWrap = document.getElementById("hist-wrap");
const histTable = document.getElementById("hist-table");
const histPage = document.getElementById("hist-page");
const histPrev = document.getElementById("hist-prev");
const histNext = document.getElementById("hist-next");
let liveData = null;
let historyPage = 1;
let historyLoaded = false;
let historyTotal = 0;

function pad(value) { return String(value).padStart(2, "0"); }
function localInput(date) {
  return date.getFullYear() + "-" + pad(date.getMonth() + 1) + "-" + pad(date.getDate())
    + "T" + pad(date.getHours()) + ":" + pad(date.getMinutes());
}
function text(value) { return value == null || value === "" ? "—" : String(value); }
function deviceList(select) {
  return Array.from(select.options).map(option => option.value);
}
const STALE_MS = 120000;
function isFresh(receivedAt) {
  if (!receivedAt) return false;
  const at = new Date(receivedAt).getTime();
  return Number.isFinite(at) && (Date.now() - at) <= STALE_MS;
}
function freshnessText(receivedAt) {
  if (!receivedAt) return "等待遥测";
  const at = new Date(receivedAt);
  if (!Number.isFinite(at.getTime())) return "等待遥测";
  const stamp = pad(at.getHours()) + ":" + pad(at.getMinutes()) + ":" + pad(at.getSeconds());
  return isFresh(receivedAt) ? ("在线 · " + stamp) : ("无更新 · " + stamp);
}
function fillSelect(select, devices, allLabel, preferred) {
  if (!select) return;
  const current = preferred != null ? preferred : select.value;
  const existing = deviceList(select);
  const next = [""].concat(devices || []);
  const same = existing.length === next.length && existing.every((value, index) => value === next[index]);
  if (!same) {
    if (typeof select.replaceChildren === "function") select.replaceChildren();
    else select.innerHTML = "";
    const all = document.createElement("option");
    all.value = "";
    all.textContent = allLabel || "全部设备";
    select.appendChild(all);
    for (const id of devices || []) {
      if (id == null || id === "") continue;
      const option = document.createElement("option");
      option.value = id;
      option.textContent = id;
      select.appendChild(option);
    }
  } else if (current && next.indexOf(current) < 0) {
    const option = document.createElement("option");
    option.value = current;
    option.textContent = current;
    select.appendChild(option);
  }
  const values = deviceList(select);
  select.value = values.indexOf(current) >= 0 ? current : "";
}
function sameDevice(a, b) {
  return String(a || "").toLowerCase() === String(b || "").toLowerCase();
}
function openDevice(deviceId) {
  if (!deviceId) return;
  selectedDeviceId = String(deviceId);
  const ids = (liveData && liveData.deviceIds && liveData.deviceIds.length)
    ? liveData.deviceIds.slice()
    : (liveData && liveData.devices ? liveData.devices.map(d => d.deviceId).filter(Boolean) : []);
  if (!ids.some(id => sameDevice(id, selectedDeviceId))) ids.push(selectedDeviceId);
  fillSelect(liveDevice, ids, "机台汇总", selectedDeviceId);
  liveDevice.value = selectedDeviceId;
  meta.textContent = "正在加载设备…";
  tick();
}
function backToFleet() {
  selectedDeviceId = "";
  liveDevice.value = "";
  liveTag.value = "";
  tick();
}

function renderPoint(tag) {
  if (!tag) return document.createTextNode("");
  const card = document.createElement("div");
  const color = tag.color || "#8AA0B5";
  if (tag.isSwitch) {
    card.className = "point switch";
    card.style.borderColor = color;
    card.style.background = tag.background || "#152536";
    const badge = document.createElement("div");
    badge.className = "badge";
    badge.style.background = color;
    badge.textContent = text(tag.indicator || tag.value);
    const body = document.createElement("div");
    body.className = "point-body";
    const name = document.createElement("div");
    name.className = "name";
    name.textContent = text(tag.name);
    const status = document.createElement("div");
    status.className = "status";
    status.style.color = color;
    status.textContent = text(tag.value);
    body.appendChild(name);
    body.appendChild(status);
    body.appendChild(renderFoot(tag));
    card.appendChild(badge);
    card.appendChild(body);
    return card;
  }

  card.className = "point meter";
  card.style.borderColor = color;
  card.style.background = tag.background || "#152536";
  const bar = document.createElement("div");
  bar.className = "meter-bar";
  bar.style.background = color;
  const body = document.createElement("div");
  body.className = "meter-body";
  const name = document.createElement("div");
  name.className = "name";
  name.textContent = text(tag.name);
  const reading = document.createElement("div");
  reading.className = "reading";
  const value = document.createElement("b");
  value.style.color = color;
  value.textContent = text(tag.value);
  reading.appendChild(value);
  if (tag.unit) {
    const unit = document.createElement("span");
    unit.className = "unit";
    unit.textContent = tag.unit;
    reading.appendChild(unit);
  }
  body.appendChild(name);
  body.appendChild(reading);
  if (tag.address) {
    const address = document.createElement("div");
    address.className = "addr";
    address.textContent = tag.address;
    body.appendChild(address);
  }
  body.appendChild(renderFoot(tag));
  card.appendChild(bar);
  card.appendChild(body);
  return card;
}

function renderFoot(tag) {
  const foot = document.createElement("div");
  foot.className = "foot";
  const dot = document.createElement("span");
  dot.className = "dot";
  dot.style.background = tag.qualityColor || "#8AA0B5";
  const time = document.createElement("span");
  time.textContent = tag.updatedAt || "";
  foot.appendChild(dot);
  foot.appendChild(time);
  return foot;
}

function renderFleet() {
  const fleetHint = document.getElementById("fleet-hint");
  const toolbar = document.getElementById("live-toolbar");
  if (fleetHint) fleetHint.hidden = false;
  if (toolbar) toolbar.hidden = true;
  clearBoard();
  const devices = (liveData.devices || []).filter(Boolean);
  if (devices.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "暂无数采机台。确认订阅已启动且采集实例在发布。";
    board.appendChild(empty);
    return;
  }
  const grid = document.createElement("div");
  grid.className = "fleet";
  for (const device of devices) {
    const fresh = isFresh(device.receivedAt);
    const color = fresh ? "#3DDC97" : "#FF6B6B";
    const card = document.createElement("div");
    card.className = "fleet-card" + (fresh ? "" : " stale");
    card.tabIndex = 0;
    card.setAttribute("role", "button");
    card.addEventListener("click", () => openDevice(device.deviceId || ""));
    card.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        openDevice(device.deviceId || "");
      }
    });
    const rail = document.createElement("div");
    rail.className = "fleet-rail";
    rail.style.background = color;
    const body = document.createElement("div");
    const top = document.createElement("div");
    top.className = "fleet-top";
    const id = document.createElement("div");
    id.className = "fleet-id";
    id.textContent = text(device.deviceId);
    top.appendChild(id);
    body.appendChild(top);
    if (device.lineName) {
      const line = document.createElement("div");
      line.className = "fleet-line";
      line.textContent = device.lineName;
      body.appendChild(line);
    }
    const freshRow = document.createElement("div");
    freshRow.className = "fleet-fresh";
    freshRow.style.color = color;
    const dot = document.createElement("span");
    dot.className = "dot";
    dot.style.background = color;
    const freshLabel = document.createElement("span");
    freshLabel.textContent = freshnessText(device.receivedAt);
    freshRow.appendChild(dot);
    freshRow.appendChild(freshLabel);
    body.appendChild(freshRow);
    const status = document.createElement("div");
    status.className = "fleet-status";
    status.textContent = device.status || "";
    body.appendChild(status);
    const switches = (device.tags || []).filter(tag => tag.isSwitch);
    if (switches.length > 0) {
      const row = document.createElement("div");
      row.className = "fleet-switches";
      for (const tag of switches) {
        const chip = document.createElement("span");
        chip.className = "fleet-chip";
        const accent = tag.color || "#8AA0B5";
        chip.style.borderColor = accent;
        chip.style.background = tag.background || "#101C28";
        const chipDot = document.createElement("span");
        chipDot.className = "chip-dot";
        chipDot.style.background = accent;
        const chipName = document.createElement("span");
        chipName.className = "chip-name";
        chipName.textContent = text(tag.name);
        const chipValue = document.createElement("span");
        chipValue.className = "chip-value";
        chipValue.style.color = accent;
        chipValue.textContent = text(tag.value);
        chip.appendChild(chipDot);
        chip.appendChild(chipName);
        chip.appendChild(chipValue);
        row.appendChild(chip);
      }
      body.appendChild(row);
    }
    const detail = document.createElement("div");
    detail.className = "fleet-detail";
    detail.textContent = (device.tagCount || 0) + " 个点位 · 点击查看全部";
    body.appendChild(detail);
    card.appendChild(rail);
    card.appendChild(body);
    grid.appendChild(card);
  }
  board.appendChild(grid);
}

function clearBoard() {
  if (!board) return;
  if (typeof board.replaceChildren === "function") board.replaceChildren();
  else board.innerHTML = "";
}
function renderLive() {
  if (!liveData) return;
  const devices = (liveData.deviceIds && liveData.deviceIds.length)
    ? liveData.deviceIds.filter(Boolean)
    : (liveData.devices || []).map(device => device && device.deviceId).filter(Boolean);
  fillSelect(liveDevice, devices, "机台汇总", selectedDeviceId);
  const selected = selectedDeviceId || (liveDevice && liveDevice.value) || "";
  if (liveDevice) liveDevice.value = selected;
  if (!selected) {
    renderFleet();
    return;
  }

  const fleetHint = document.getElementById("fleet-hint");
  const toolbar = document.getElementById("live-toolbar");
  if (fleetHint) fleetHint.hidden = true;
  if (toolbar) toolbar.hidden = false;
  const keyword = ((liveTag && liveTag.value) || "").trim().toLowerCase();
  clearBoard();
  let shown = 0;
  for (const device of liveData.devices || []) {
    if (!device || !sameDevice(device.deviceId, selected)) continue;
    const tags = (device.tags || []).filter(tag =>
      tag && (!keyword || text(tag.name).toLowerCase().indexOf(keyword) >= 0));
    if (keyword && tags.length === 0) continue;
    shown++;
    const section = document.createElement("section");
    const head = document.createElement("div");
    head.className = "device-head";
    const left = document.createElement("div");
    const title = document.createElement("h2");
    title.textContent = text(device.deviceId);
    const topic = document.createElement("div");
    topic.className = "topic";
    topic.textContent = device.status || "";
    left.appendChild(title);
    left.appendChild(topic);
    head.appendChild(left);
    section.appendChild(head);
    let group = null;
    let cards = null;
    let count = null;
    let groupSize = 0;
    function closeGroup() {
      if (count) count.textContent = "(" + groupSize + ")";
    }
    for (const tag of tags) {
      if (!tag) continue;
      const groupName = tag.group || "";
      if (!group || group.getAttribute("data-name") !== groupName) {
        closeGroup();
        groupSize = 0;
        const block = document.createElement("div");
        const label = document.createElement("div");
        label.className = "group-head";
        label.setAttribute("data-name", groupName);
        const bar = document.createElement("span");
        bar.className = "group-bar";
        bar.style.background = tag.groupColor || "#8AA0B5";
        const name = document.createElement("span");
        name.className = "group-title";
        name.style.color = tag.groupColor || "#8AA0B5";
        name.textContent = groupName;
        count = document.createElement("span");
        count.className = "group-count";
        label.appendChild(bar);
        label.appendChild(name);
        label.appendChild(count);
        cards = document.createElement("div");
        cards.className = "cards";
        block.appendChild(label);
        block.appendChild(cards);
        section.appendChild(block);
        group = label;
      }
      groupSize++;
      if (cards) cards.appendChild(renderPoint(tag));
    }
    closeGroup();
    board.appendChild(section);
  }
  if (shown === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = (liveData.devices || []).length === 0 ? "暂无订阅数据" : "没有匹配的设备或点位";
    board.appendChild(empty);
  }
}

function historyParams(page) {
  const query = new URLSearchParams();
  if (histDevice.value) query.set("device", histDevice.value);
  if (histFrom.value) query.set("from", histFrom.value);
  if (histTo.value) query.set("to", histTo.value);
  if (histTag.value.trim()) query.set("tag", histTag.value.trim());
  query.set("page", String(page));
  return query;
}

function renderHistory(data) {
  historyTotal = data.total || 0;
  historyPage = data.page || 1;
  fillSelect(histDevice, data.devices || []);
  if (data.device) histDevice.value = data.device;
  const pages = Math.max(1, Math.ceil(historyTotal / (data.pageSize || 40)));
  let summary = "共 " + historyTotal + " 条";
  if (historyTotal > 0) summary += " · 第 " + historyPage + "/" + pages + " 页";
  if (data.columnsTruncated) summary += " · 只显示前 64 个点位";
  if (!data.recording) summary += " · 未开启历史记录，新数据不会写入";
  histSummary.textContent = summary;
  histPrev.disabled = historyPage <= 1;
  histNext.disabled = historyPage >= pages || historyTotal === 0;
  histPage.textContent = historyPage + " / " + pages;
  const rows = data.rows || [];
  if (rows.length === 0) {
    histWrap.hidden = true;
    histEmpty.hidden = false;
    histEmpty.textContent = data.tag ? "没有匹配的历史数据" : "当前筛选条件下没有历史数据";
    histTable.replaceChildren();
    return;
  }
  histEmpty.hidden = true;
  histWrap.hidden = false;
  histTable.replaceChildren();
  const head = document.createElement("thead");
  const headRow = document.createElement("tr");
  for (const title of ["时间", "设备"].concat(data.columns || [])) {
    const cell = document.createElement("th");
    cell.textContent = title;
    headRow.appendChild(cell);
  }
  head.appendChild(headRow);
  histTable.appendChild(head);
  const body = document.createElement("tbody");
  for (const row of rows) {
    const tr = document.createElement("tr");
    const time = document.createElement("td");
    time.textContent = row.time || "";
    const device = document.createElement("td");
    device.textContent = row.device || "";
    tr.appendChild(time);
    tr.appendChild(device);
    for (const value of row.values || []) {
      const cell = document.createElement("td");
      cell.textContent = value || "—";
      tr.appendChild(cell);
    }
    body.appendChild(tr);
  }
  histTable.appendChild(body);
}

async function loadHistory(page) {
  banner.textContent = "";
  histSummary.textContent = "正在查询…";
  const response = await fetch("/api/history?" + historyParams(page).toString(), { cache: "no-store" });
  if (response.status === 401) { location.href = "/"; return; }
  if (!response.ok) {
    histSummary.textContent = "";
    banner.textContent = await response.text();
    return;
  }
  historyLoaded = true;
  renderHistory(await response.json());
}

async function exportHistory() {
  const button = document.getElementById("hist-export");
  button.disabled = true;
  banner.textContent = "";
  try {
    const query = historyParams(historyPage);
    query.delete("page");
    const response = await fetch("/api/history/export?" + query.toString(), { cache: "no-store" });
    if (response.status === 401) { location.href = "/"; return; }
    if (!response.ok) {
      banner.textContent = await response.text();
      return;
    }
    const blob = await response.blob();
    const header = response.headers.get("Content-Disposition") || "";
    const matched = header.match(/filename\*=UTF-8''([^;]+)/i);
    const name = matched ? decodeURIComponent(matched[1]) : "历史数据.xlsx";
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = name;
    link.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    banner.textContent = "导出失败";
  } finally {
    button.disabled = false;
  }
}

function showTab(tab) {
  document.getElementById("live").hidden = tab !== "live";
  document.getElementById("history").hidden = tab !== "history";
  document.getElementById("tab-live").classList.toggle("on", tab === "live");
  document.getElementById("tab-history").classList.toggle("on", tab === "history");
  if (tab === "history" && !historyLoaded) loadHistory(1).catch(() => { banner.textContent = "无法连接订阅服务"; });
}

let selectedDeviceId = "";
let tickInflight = false;
let tickAgain = false;
async function tick() {
  if (tickInflight) {
    tickAgain = true;
    return;
  }
  tickInflight = true;
  try {
    do {
      tickAgain = false;
      try {
        // 汇总页不拉全量点位；进入单机后再带 ?device=
        const selected = selectedDeviceId;
        const url = selected
          ? "/api/status?device=" + encodeURIComponent(selected)
          : "/api/status";
        const response = await fetch(url, { cache: "no-store" });
        if (response.status === 401) { location.href = "/"; return; }
        if (response.status === 503) {
          meta.textContent = liveData ? "服务繁忙，正在重试…" : "无法连接订阅服务";
          continue;
        }
        if (!response.ok) {
          const message = (await response.text()) || ("HTTP " + response.status);
          meta.textContent = liveData ? ("加载失败：" + message) : "无法连接订阅服务";
          banner.textContent = message;
          continue;
        }
        liveData = await response.json();
        const when = liveData.generatedAt ? new Date(liveData.generatedAt).toLocaleString() : "";
        const run = liveData.running ? "订阅运行中" : "订阅未运行";
        const mqtt = liveData.mqttConnected ? "MQTT 已连接" : "MQTT 未连接";
        meta.textContent = [liveData.lineName, run, mqtt, when].filter(Boolean).join(" · ");
        if (document.getElementById("history").hidden) banner.textContent = liveData.lastError || "";
        // 若等待期间又点了别的机台，丢弃本次渲染，立刻按新目标再拉。
        if (tickAgain) continue;
        try {
          renderLive();
        } catch (renderError) {
          meta.textContent = "页面渲染失败，请刷新或返回汇总";
          banner.textContent = (renderError && renderError.message) ? String(renderError.message) : "渲染异常";
          try { clearBoard(); } catch (_) {}
        }
      } catch (error) {
        meta.textContent = liveData
          ? "连接中断，正在重试…"
          : "无法连接订阅服务";
      }
    } while (tickAgain);
  } finally {
    tickInflight = false;
  }
}

const now = new Date();
histTo.value = localInput(now);
histFrom.value = localInput(new Date(now.getTime() - 24 * 60 * 60 * 1000));
document.getElementById("tab-live").addEventListener("click", () => {
  backToFleet();
  showTab("live");
});
document.getElementById("tab-history").addEventListener("click", () => showTab("history"));
document.getElementById("live-back").addEventListener("click", backToFleet);
liveDevice.addEventListener("change", () => {
  selectedDeviceId = liveDevice.value || "";
  tick();
});
liveTag.addEventListener("input", renderLive);
document.getElementById("hist-query").addEventListener("click", () => loadHistory(1).catch(() => { banner.textContent = "无法连接订阅服务"; }));
document.getElementById("hist-export").addEventListener("click", exportHistory);
document.getElementById("logout").addEventListener("click", async () => {
  await fetch("/api/logout", { method: "POST" });
  location.href = "/";
});
histPrev.addEventListener("click", () => { if (historyPage > 1) loadHistory(historyPage - 1); });
histNext.addEventListener("click", () => loadHistory(historyPage + 1));
tick();
setInterval(tick, 2500);
</script>
</body>
</html>
""";
}
