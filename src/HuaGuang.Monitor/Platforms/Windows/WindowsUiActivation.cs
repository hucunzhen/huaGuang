using System.IO.Pipes;
using System.Text;
using Microsoft.Maui.Controls;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>第二次启动时唤醒已隐藏/在后台的主窗口，而不是再开一份或静默退出。</summary>
static class WindowsUiActivation
{
    const string PipeName = "HuaGuang.Monitor.Ui.Activate";
    static CancellationTokenSource? _serverCts;

    public static bool TryActivateExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None);
            client.Connect(1500);
            var payload = Encoding.UTF8.GetBytes("show");
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StartServer(Window window)
    {
        StopServer();
        _serverCts = new CancellationTokenSource();
        var token = _serverCts.Token;
        window.Destroying += (_, _) => StopServer();

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    var buffer = new byte[16];
                    _ = await server.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    MainThread.BeginInvokeOnMainThread(() => WindowsMainWindowPresenter.TryShow(window));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }, token);
    }

    static void StopServer()
    {
        try
        {
            _serverCts?.Cancel();
            _serverCts?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _serverCts = null;
        }
    }
}

static class WindowsMainWindowPresenter
{
    public static void TryShow(Window window)
    {
        try
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                nativeWindow.AppWindow.Show();
                nativeWindow.Activate();
                return;
            }

            window.HandlerChanged += OnHandlerChanged;
        }
        catch
        {
        }

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                window.HandlerChanged -= OnHandlerChanged;
                nativeWindow.AppWindow.Show();
                nativeWindow.Activate();
            }
        }
    }
}
