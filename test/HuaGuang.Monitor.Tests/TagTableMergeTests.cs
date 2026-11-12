using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class TagTableMergeTests
{
    [Fact]
    public void Keeps_new_disk_rows_and_applies_only_the_edited_point()
    {
        var loaded = new List<PlcTag> { Tag("车速", "D18"), Tag("注胶量", "D1") };
        var edited = new List<PlcTag> { Tag("车速", "D99"), Tag("注胶量", "D1") };
        var disk = new List<PlcTag> { Tag("车速", "D18"), Tag("注胶量", "D1"), Tag("切刀间距", "") };

        var merged = LineExcelConfigService.MergeDiskTagsWithEdits(Snapshot(loaded), edited, disk);

        Assert.Equal(["车速", "注胶量", "切刀间距"], merged.Select(tag => tag.Name).ToArray());
        Assert.Equal("D99", merged[0].XinjeAddress);
        Assert.Equal("D1", merged[1].XinjeAddress);
    }

    [Fact]
    public void Unedited_save_does_not_restore_rows_removed_from_the_new_table()
    {
        var loaded = new List<PlcTag> { Tag("车速", "D18"), Tag("旧点", "D2") };
        var disk = new List<PlcTag> { Tag("车速", "DB55.DBW0"), Tag("切刀间距", "") };

        var merged = LineExcelConfigService.MergeDiskTagsWithEdits(Snapshot(loaded), loaded, disk);

        Assert.Equal(["车速", "切刀间距"], merged.Select(tag => tag.Name).ToArray());
        Assert.Equal("DB55.DBW0", merged[0].XinjeAddress);
    }

    static List<(string Name, string Signature)> Snapshot(IEnumerable<PlcTag> tags) =>
        tags.Select(tag => (tag.Name, LineExcelConfigService.TagEditSignature(tag))).ToList();

    static PlcTag Tag(string name, string address) => new()
    {
        Name = name,
        Source = string.IsNullOrEmpty(address) ? TagSource.Manual : TagSource.Plc,
        XinjeAddress = string.IsNullOrEmpty(address) ? "D0" : address,
        DataType = TagDataType.Float32
    };
}
