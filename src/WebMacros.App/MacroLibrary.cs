using System.IO;
using WebMacros.Engine.Samples;

namespace WebMacros.App;

/// <summary>Folders under %USERPROFILE%\Documents\WebMacros and sample seeding.</summary>
public sealed class MacroLibrary
{
    public string Root { get; }
    public string MacrosFolder => Path.Combine(Root, "Macros");
    public string DataSourcesFolder => Path.Combine(Root, "Datasources");
    public string DownloadsFolder => Path.Combine(Root, "Downloads");

    public MacroLibrary(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WebMacros");
    }

    /// <summary>Creates the folders; on first run (no macros folder yet) seeds the sample macros and datasource.</summary>
    public void EnsureCreated()
    {
        var firstRun = !Directory.Exists(MacrosFolder);
        Directory.CreateDirectory(MacrosFolder);
        Directory.CreateDirectory(DataSourcesFolder);
        Directory.CreateDirectory(DownloadsFolder);
        if (firstRun || !Directory.EnumerateFiles(MacrosFolder, "*.iim").Any())
        {
            foreach (var s in SampleMacros.Macros)
                File.WriteAllText(Path.Combine(MacrosFolder, s.FileName), s.Content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }
        foreach (var d in SampleMacros.DataSources)
        {
            var path = Path.Combine(DataSourcesFolder, d.FileName);
            if (!File.Exists(path)) File.WriteAllText(path, d.Content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }
    }

    public IReadOnlyList<string> ListMacros() =>
        Directory.Exists(MacrosFolder)
            ? Directory.EnumerateFiles(MacrosFolder, "*.iim", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();

    public string PathFor(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (!name.EndsWith(".iim", StringComparison.OrdinalIgnoreCase)) name += ".iim";
        return Path.Combine(MacrosFolder, name);
    }
}
