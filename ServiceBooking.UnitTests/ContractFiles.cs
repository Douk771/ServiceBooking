using System.Text.Json;

namespace ServiceBooking.UnitTests;

/// <summary>Reads a committed contracts/&lt;cycle&gt;/… file straight off disk (walking up from the test output folder).</summary>
internal static class ContractFiles
{
    public static JsonDocument Load(string cycle, string fileName)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "contracts", cycle, fileName);
            if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate));
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"contracts/{cycle}/{fileName} not found above {AppContext.BaseDirectory}");
    }
}
