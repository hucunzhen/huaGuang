using System.Net;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class WebLoginAccountTests
{
    [Fact]
    public void Empty_account_uses_the_visible_default()
    {
        var settings = new AppSettings
        {
            WebLoginUsername = "",
            WebLoginPassword = ""
        };

        WebLoginAccount.Ensure(settings);

        Assert.Equal(WebLoginAccount.DefaultUsername, settings.WebLoginUsername);
        Assert.Equal("123456", settings.WebLoginPassword);
        Assert.Equal("123456", WebLoginAccount.DefaultPassword);
        Assert.True(WebLoginAccount.Matches(settings, "admin", "123456"));
        Assert.False(WebLoginAccount.Matches(settings, "admin", "1234567"));
    }

    [Fact]
    public void Reset_restores_only_the_default_password()
    {
        var settings = new AppSettings
        {
            WebLoginUsername = "operator",
            WebLoginPassword = "changed"
        };

        WebLoginAccount.ResetPassword(settings);

        Assert.Equal("operator", settings.WebLoginUsername);
        Assert.Equal(WebLoginAccount.DefaultPassword, settings.WebLoginPassword);
    }

    [Fact]
    public void Line_excel_keeps_the_web_login()
    {
        var path = Path.Combine(Path.GetTempPath(), $"huaguang-web-login-{Guid.NewGuid():N}.xlsx");
        try
        {
            var settings = new AppSettings
            {
                LineName = "订阅大屏",
                WebLoginUsername = "operator",
                WebLoginPassword = "secret-1"
            };
            LineExcelConfigService.Export(settings, path);
            var loaded = LineExcelConfigService.LoadLineExcelFromFile(path, null, "订阅大屏");
            Assert.Equal("operator", loaded.WebLoginUsername);
            Assert.Equal("secret-1", loaded.WebLoginPassword);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Session_cookie_roundtrip()
    {
        var token = SubscribeWebSessions.Issue();
        Assert.True(SubscribeWebSessions.IsValid(token));
        Assert.Equal(token, SubscribeWebSessions.ReadCookie($"other=1; {SubscribeWebSessions.CookieName}={token}"));
        var form = SubscribeWebSessions.ParseForm("username=admin&password=123456");
        Assert.Equal("admin", form["username"]);
        Assert.Equal("123456", form["password"]);
        SubscribeWebSessions.Revoke(token);
        Assert.False(SubscribeWebSessions.IsValid(token));
    }

    [Fact]
    public void Active_clients_follow_recent_page_loads()
    {
        var before = SubscribeWebSessions.ActiveCount;
        var first = SubscribeWebSessions.Issue();
        var second = SubscribeWebSessions.Issue();
        try
        {
            Assert.Equal(before + 2, SubscribeWebSessions.ActiveCount);
            SubscribeWebSessions.Touch(first, DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal(before + 1, SubscribeWebSessions.ActiveCount);
            SubscribeWebSessions.Revoke(second);
            Assert.Equal(before, SubscribeWebSessions.ActiveCount);
        }
        finally
        {
            SubscribeWebSessions.Revoke(first);
            SubscribeWebSessions.Revoke(second);
        }
    }

    [Fact]
    public async Task Browser_must_log_in_before_the_dashboard()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-web-login-{Guid.NewGuid():N}.db");
        var store = new HistoryStore(dbPath);
        await store.InitializeAsync();
        var settings = new AppSettings
        {
            LineName = "订阅大屏",
            WebLoginUsername = WebLoginAccount.DefaultUsername,
            WebLoginPassword = WebLoginAccount.DefaultPassword
        };
        var model = SubscribeWebHttp.CreateReadModel(
            _ => new SubscribeWebStatus { Running = true, MqttConnected = true, LineName = "订阅大屏" },
            () => settings,
            store);
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:18090/");
        listener.Start();
        using var cts = new CancellationTokenSource();
        var serve = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception) when (cts.IsCancellationRequested || !listener.IsListening)
                {
                    break;
                }

                _ = Task.Run(() => SubscribeWebHttp.HandleAsync(context, model, new SemaphoreSlim(1, 1), cts.Token));
            }
        });

        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            var anonymous = await client.GetStringAsync("http://127.0.0.1:18090/");
            Assert.Contains("登录", anonymous);
            Assert.DoesNotContain("退出", anonymous);

            var rejected = await client.PostAsync(
                "http://127.0.0.1:18090/api/login",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "admin", ["password"] = "nope" }));
            Assert.Contains("用户名或密码不正确", await rejected.Content.ReadAsStringAsync());

            var accepted = await client.PostAsync(
                "http://127.0.0.1:18090/api/login",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "Admin", ["password"] = "123456" }));
            Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
            var cookie = accepted.Headers.GetValues("Set-Cookie").Single();
            Assert.Contains("huaguang_web=", cookie);

            using var dashboard = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:18090/");
            dashboard.Headers.Add("Cookie", cookie.Split(';')[0]);
            var page = await client.SendAsync(dashboard);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("退出", html);
        }
        finally
        {
            cts.Cancel();
            listener.Close();
            try
            {
                await serve;
            }
            catch (Exception)
            {
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var shard = Path.Combine(Path.GetDirectoryName(dbPath)!, Path.GetFileNameWithoutExtension(dbPath)!);
            if (Directory.Exists(shard))
            {
                Directory.Delete(shard, true);
            }
        }
    }
}
