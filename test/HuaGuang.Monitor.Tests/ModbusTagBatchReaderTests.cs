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
    public void PlannedReads_SplitSparseTagsIntoSeparateChunks()
    {
        var tags = CreateXianheChunkTags();
        var chunks = ModbusTagBatchReader.GetPlannedRegisterChunks(tags);

        // D1000 / D1090 / D1130 间距大于 MaxRegisterGap，应各自独立读取，避免一次扫过大片空区。
        Assert.Equal(3, chunks.Count);
        Assert.Contains(chunks, chunk => chunk.TagNames.Contains("运行状态", StringComparer.Ordinal));
        Assert.Contains(chunks, chunk => chunk.TagNames.Contains("卷曲张力", StringComparer.Ordinal));
        Assert.Contains(chunks, chunk => chunk.TagNames.Contains("当前注胶机编号", StringComparer.Ordinal));

        var injection = chunks.First(chunk =>
            chunk.TagNames.Contains("当前注胶机编号", StringComparer.Ordinal));
        Assert.Equal(1130, injection.StartAddress);
        Assert.Equal(1, injection.RegisterCount);
    }

    [Fact]
    public void PlannedReads_KeepSafenSparseBlocksSeparate()
    {
        var tags = new List<PlcTag>
        {
            new() { Name = "运行状态", XinjeAddress = "D1000", DataType = TagDataType.Int16 },
            new() { Name = "主辊筒车速", XinjeAddress = "D1030", DataType = TagDataType.Float32 },
            new() { Name = "烘箱温度1", XinjeAddress = "D6000", DataType = TagDataType.Float32 }
        };

        var chunks = ModbusTagBatchReader.GetPlannedRegisterChunks(tags);
        Assert.Equal(3, chunks.Count);
        Assert.Contains(chunks, c => c.StartAddress == 1000 && c.RegisterCount == 1);
        Assert.Contains(chunks, c => c.StartAddress == 1030 && c.RegisterCount == 2);
        Assert.Contains(chunks, c => c.StartAddress == 6000 && c.RegisterCount == 2);
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
