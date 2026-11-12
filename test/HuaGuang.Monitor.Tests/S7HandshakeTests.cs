using HuaGuang.Monitor.Protocols;
using S7.Net;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class S7HandshakeTests
{
    [Fact]
    public void S7200Smart_uses_s71200_handshake_and_slot_1()
    {
        var handshake = S7PlcClient.ResolveHandshake("S7200SMART", rack: 0, slot: 0);
        Assert.Equal(CpuType.S71200, handshake.Cpu);
        Assert.Equal((short)0, handshake.Rack);
        Assert.Equal((short)1, handshake.Slot);
    }

    [Fact]
    public void Classic_s7200_stays_s7200()
    {
        var handshake = S7PlcClient.ResolveHandshake("S7-200", rack: 0, slot: 0);
        Assert.Equal(CpuType.S7200, handshake.Cpu);
        Assert.Equal((short)0, handshake.Slot);
    }
}
