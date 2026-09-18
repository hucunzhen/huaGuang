using System;
using ClosedXML.Excel;
var path = args[0];
using var wb = new XLWorkbook(path);
var sheet = wb.Worksheet("配置");
var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
for (var r = 2; r <= last; r++) {
  var key = sheet.Cell(r, 1).GetString().Trim();
  if (key == "PLC协议") sheet.Cell(r, 2).Value = "Modbus TCP";
  if (key == "PLC端口") sheet.Cell(r, 2).Value = 502;
}
wb.Save();
Console.WriteLine("Repaired " + path);
