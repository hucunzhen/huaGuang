using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class S7ConnectTimeoutTests
{
    [Fact]
    public async Task Silent_peer_connect_ends_within_configured_timeout()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var accept = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    _ = await listener.AcceptTcpClientAsync(stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var client = new S7PlcClient(NullLogger<S7PlcClient>.Instance);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var error = await Record.ExceptionAsync(() => client.ConnectAsync(new PlcSettings
            {
                Protocol = PlcProtocol.S7,
                Host = "127.0.0.1",
                Port = port,
                CpuType = "S71500",
                Rack = 0,
                Slot = 0,
                TimeoutMs = 500
            }, CancellationToken.None));

            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Assert.NotNull(error);
            Assert.IsType<InvalidOperationException>(error);
            Assert.Contains("超过 500 ms", error.Message, StringComparison.Ordinal);
            Assert.InRange(elapsedMs, 300, 2500);
            Assert.False(client.IsConnected);
        }
        finally
        {
            await client.DisposeAsync();
            stop.Cancel();
            listener.Stop();
            try
            {
                await accept;
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Cancelling_connect_unblocks_without_waiting_out_the_timeout()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var accept = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    _ = await listener.AcceptTcpClientAsync(stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var client = new S7PlcClient(NullLogger<S7PlcClient>.Instance);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var connectCancel = new CancellationTokenSource(300);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var error = await Record.ExceptionAsync(() => client.ConnectAsync(new PlcSettings
            {
                Protocol = PlcProtocol.S7,
                Host = "127.0.0.1",
                Port = port,
                CpuType = "S71500",
                TimeoutMs = 10_000
            }, connectCancel.Token));

            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Assert.IsType<OperationCanceledException>(error);
            Assert.InRange(elapsedMs, 0, 2000);
            Assert.False(client.IsConnected);
        }
        finally
        {
            await client.DisposeAsync();
            stop.Cancel();
            listener.Stop();
            try
            {
                await accept;
            }
            catch
            {
            }
        }
    }
}
