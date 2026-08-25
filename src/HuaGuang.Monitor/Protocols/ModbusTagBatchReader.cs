using HuaGuang.Monitor.Models;

using NModbus;



namespace HuaGuang.Monitor.Protocols;



internal static class ModbusTagBatchReader

{

    const int MaxRegisterGap = 120;

    const int MaxRegistersPerRead = 125;



    public static Dictionary<string, object> ReadTags(

        IModbusMaster master,

        byte station,

        IReadOnlyList<PlcTag> tags,

        int timeoutMs,

        Action? onTimeout = null)

    {

        var resolved = new List<ResolvedTagRead>(tags.Count);

        foreach (var tag in tags)

        {

            resolved.Add(Resolve(tag));

        }



        var result = new Dictionary<string, object>(tags.Count, StringComparer.Ordinal);

        foreach (var tableGroup in resolved.GroupBy(item => item.Table))

        {

            if (tableGroup.Key is ModbusTable.Coil or ModbusTable.DiscreteInput)

            {

                ReadCoilGroup(master, station, tableGroup.Key, tableGroup, timeoutMs, onTimeout, result);

                continue;

            }



            ReadRegisterGroup(master, station, tableGroup.Key, tableGroup, timeoutMs, onTimeout, result);

        }



        return result;

    }



    static void ReadRegisterGroup(

        IModbusMaster master,

        byte station,

        ModbusTable table,

        IEnumerable<ResolvedTagRead> tags,

        int timeoutMs,

        Action? onTimeout,

        IDictionary<string, object> result)

    {

        foreach (var block in PlanRegisterBlocks(tags))

        {

            var registers = ModbusIoTimeout.Run(

                () => table == ModbusTable.InputRegister

                    ? master.ReadInputRegisters(station, block.StartAddress, block.RegisterCount)

                    : master.ReadHoldingRegisters(station, block.StartAddress, block.RegisterCount),

                timeoutMs,

                onTimeout);



            foreach (var item in block.Items)

            {

                var slice = new ushort[item.RegisterCount];

                Array.Copy(registers, item.Offset, slice, 0, item.RegisterCount);

                var raw = RegisterConverter.ToValue(slice, item.DataType, item.ByteOrder);

                result[item.Name] = item.DataType == TagDataType.Bool

                    ? raw

                    : RegisterConverter.ApplyScale(raw, item.SourceTag);

            }

        }

    }



    static void ReadCoilGroup(

        IModbusMaster master,

        byte station,

        ModbusTable table,

        IEnumerable<ResolvedTagRead> tags,

        int timeoutMs,

        Action? onTimeout,

        IDictionary<string, object> result)

    {

        foreach (var block in PlanCoilBlocks(tags))

        {

            var coils = ModbusIoTimeout.Run(

                () => table == ModbusTable.DiscreteInput

                    ? master.ReadInputs(station, block.StartAddress, block.CoilCount)

                    : master.ReadCoils(station, block.StartAddress, block.CoilCount),

                timeoutMs,

                onTimeout);



            foreach (var item in block.Items)

            {

                result[item.Name] = coils[item.Offset];

            }

        }

    }



    static IEnumerable<RegisterReadBlock> PlanRegisterBlocks(IEnumerable<ResolvedTagRead> tags)

    {

        var ordered = tags.OrderBy(tag => tag.Address).ToList();

        RegisterReadBlock? current = null;



        foreach (var tag in ordered)

        {

            if (current is null || tag.Address > current.EndAddress + MaxRegisterGap)

            {

                foreach (var finalized in FlushRegisterBlock(current))

                {

                    yield return finalized;

                }



                current = new RegisterReadBlock(tag.Address, tag.EndAddress);

                current.Items.Add(CreateRegisterItem(tag, current.StartAddress));

                continue;

            }



            current.EndAddress = Math.Max(current.EndAddress, tag.EndAddress);

            current.Items.Add(CreateRegisterItem(tag, current.StartAddress));

        }



        foreach (var finalized in FlushRegisterBlock(current))

        {

            yield return finalized;

        }

    }



    static IEnumerable<RegisterReadBlock> FlushRegisterBlock(RegisterReadBlock? block)

    {

        if (block is null)

        {

            yield break;

        }



        var start = block.StartAddress;

        var end = block.EndAddress;

        var items = block.Items;



        while (start <= end)

        {

            var span = Math.Min(end - start + 1, MaxRegistersPerRead);

            var chunkEnd = (ushort)(start + span - 1);

            var chunk = new RegisterReadBlock(start, chunkEnd)

            {

                RegisterCount = (ushort)span

            };



            foreach (var item in items)

            {

                var tagStart = (ushort)(start + item.Offset);

                var tagEnd = (ushort)(tagStart + item.RegisterCount - 1);

                if (tagEnd > chunkEnd || tagStart < start)

                {

                    continue;

                }



                chunk.Items.Add(new RegisterBlockItem(

                    item.Name,

                    item.SourceTag,

                    tagStart - start,

                    item.RegisterCount,

                    item.DataType,

                    item.ByteOrder));

            }



            if (chunk.Items.Count > 0)

            {

                yield return chunk;

            }



            start = (ushort)(chunkEnd + 1);

        }

    }



    static IEnumerable<CoilReadBlock> PlanCoilBlocks(IEnumerable<ResolvedTagRead> tags)

    {

        var ordered = tags.OrderBy(tag => tag.Address).ToList();

        CoilReadBlock? current = null;



        foreach (var tag in ordered)

        {

            if (current is null || tag.Address > current.EndAddress + MaxRegisterGap)

            {

                if (current is not null)

                {

                    yield return current;

                }



                current = new CoilReadBlock(tag.Address, tag.Address);

                current.Items.Add(CreateCoilItem(tag, current.StartAddress));

                continue;

            }



            current.EndAddress = Math.Max(current.EndAddress, tag.Address);

            current.Items.Add(CreateCoilItem(tag, current.StartAddress));

        }



        if (current is not null)

        {

            current.CoilCount = (ushort)(current.EndAddress - current.StartAddress + 1);

            yield return current;

        }

    }



    static RegisterBlockItem CreateRegisterItem(ResolvedTagRead tag, ushort blockStart) =>

        new(tag.Name, tag.SourceTag, tag.Address - blockStart, tag.RegisterCount, tag.DataType, tag.ByteOrder);



    static CoilBlockItem CreateCoilItem(ResolvedTagRead tag, ushort blockStart) =>

        new(tag.Name, (ushort)(tag.Address - blockStart));



    static ResolvedTagRead Resolve(PlcTag tag)

    {

        var table = tag.Table;

        var address = tag.Address;

        var dataType = tag.DataType;



        if (!string.IsNullOrWhiteSpace(tag.XinjeAddress))

        {

            if (!XinjeXd5eMapper.TryResolve(tag.XinjeAddress, out var resolved, out var error))

            {

                throw new InvalidOperationException(error);

            }



            table = resolved.Table;

            address = resolved.Address;

            if (resolved.IsBit)

            {

                dataType = TagDataType.Bool;

            }

        }



        var registerCount = table is ModbusTable.Coil or ModbusTable.DiscreteInput

            ? (ushort)1

            : (ushort)RegisterConverter.RegisterCount(dataType);



        return new ResolvedTagRead(tag, tag.Name, table, address, (ushort)(address + registerCount - 1), registerCount, dataType, tag.ByteOrder);

    }



    sealed class RegisterReadBlock(ushort startAddress, ushort endAddress)

    {

        public ushort StartAddress { get; } = startAddress;

        public ushort EndAddress { get; set; } = endAddress;

        public ushort RegisterCount { get; set; }

        public List<RegisterBlockItem> Items { get; } = [];

    }



    sealed class CoilReadBlock(ushort startAddress, ushort endAddress)

    {

        public ushort StartAddress { get; } = startAddress;

        public ushort EndAddress { get; set; } = endAddress;

        public ushort CoilCount { get; set; } = 1;

        public List<CoilBlockItem> Items { get; } = [];

    }



    readonly record struct RegisterBlockItem(

        string Name,

        PlcTag SourceTag,

        int Offset,

        int RegisterCount,

        TagDataType DataType,

        ByteOrder ByteOrder);



    readonly record struct CoilBlockItem(string Name, int Offset);



    readonly record struct ResolvedTagRead(

        PlcTag SourceTag,

        string Name,

        ModbusTable Table,

        ushort Address,

        ushort EndAddress,

        ushort RegisterCount,

        TagDataType DataType,

        ByteOrder ByteOrder);

}

