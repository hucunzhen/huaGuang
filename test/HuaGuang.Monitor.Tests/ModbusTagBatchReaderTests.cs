using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public class ModbusTagBatchReaderTests
{
    [Fact]
    public void PlannedReads_IncludeRegistersBeyondFirstChunk()
    {
        var tags = CreateXianheChunkTags();
        var planned = ModbusTagBatchReader.GetPlannedTagNames(tags);

        Assert.Contains("当前注胶机编号", planned);
        Assert.Contains("运行状态", planned);
        Assert.Contains("卷曲张力", planned);
    }

    [Fact]
    public void PlannedReads_ScheduleSecondChunkForD1130()
    {
        var tags = CreateXianheChunkTags();
        var chunks = ModbusTagBatchReader.GetPlannedRegisterChunks(tags);

        var secondChunk = chunks.FirstOrDefault(chunk =>
            chunk.TagNames.Contains("当前注胶机编号", StringComparer.Ordinal));
        Assert.NotEqual(default(ModbusTagBatchReader.PlannedRegisterChunk), secondChunk);
        Assert.Equal(1125, secondChunk.StartAddress);
        Assert.Equal(6, secondChunk.RegisterCount);
    }

    [Fact]
    public void LineCatalog_IncludesCurrentInjectionMachineTag()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);

        var tag = settings.Tags.FirstOrDefault(item => item.Name == "当前注胶机编号");
        Assert.NotNull(tag);
        Assert.Equal("D1130", tag!.XinjeAddress);
        Assert.Equal(TagDataType.Int16, tag.DataType);
        Assert.False(tag.IsManual);
        Assert.True(tag.Enabled);
    }

    static List<PlcTag> CreateXianheChunkTags() =>
    [
        new()
        {
            Name = "运行状态",
            XinjeAddress = "D1000",
            DataType = TagDataType.Int16
        },
        new()
        {
            Name = "卷曲张力",
            XinjeAddress = "D1090",
            DataType = TagDataType.Float32,
            ByteOrder = ByteOrder.CDAB
        },
        new()
        {
            Name = "当前注胶机编号",
            XinjeAddress = "D1130",
            DataType = TagDataType.Int16
        }
    ];
}
