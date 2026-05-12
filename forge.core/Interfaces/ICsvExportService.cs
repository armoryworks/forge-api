namespace Forge.Core.Interfaces;

public interface ICsvExportService
{
    byte[] Export<T>(IEnumerable<T> records);
}
