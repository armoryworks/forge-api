using System.Globalization;

using CsvHelper;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public class CsvExportService : ICsvExportService
{
    public byte[] Export<T>(IEnumerable<T> records)
    {
        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(records);
        }
        return memoryStream.ToArray();
    }
}
