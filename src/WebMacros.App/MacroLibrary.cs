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

    /// <summary>File extensions shown in the macro list: iMacros macros and JavaScript macros.</summary>
    public static readonly string[] MacroExtensions = { ".iim", ".js" };

    private string SeededMarker => Path.Combine(MacrosFolder, ".samples-seeded");

    /// <summary>
    /// Creates the folders and seeds each sample macro/script once. Seeded names are remembered in
    /// Macros\.samples-seeded, so samples added in newer versions appear for existing installs while samples
    /// the user deleted are not brought back.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(MacrosFolder);
        Directory.CreateDirectory(DataSourcesFolder);
        Directory.CreateDirectory(DownloadsFolder);

        var seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(SeededMarker))
            foreach (var line in File.ReadAllLines(SeededMarker))
                if (!string.IsNullOrWhiteSpace(line)) seeded.Add(line.Trim());
        var changed = false;
        foreach (var s in SampleMacros.Macros.Concat(SampleMacros.Scripts))
        {
            if (seeded.Contains(s.FileName)) continue;
            var path = Path.Combine(MacrosFolder, s.FileName);
            if (!File.Exists(path)) File.WriteAllText(path, s.Content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            seeded.Add(s.FileName);
            changed = true;
        }
        if (changed) File.WriteAllLines(SeededMarker, seeded.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        foreach (var d in SampleMacros.DataSources)
        {
            var path = Path.Combine(DataSourcesFolder, d.FileName);
            if (!File.Exists(path)) File.WriteAllText(path, d.Content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }
    }

    public static bool IsScript(string? path) => path is not null && path.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> ListMacros() =>
        Directory.Exists(MacrosFolder)
            ? Directory.EnumerateFiles(MacrosFolder, "*", SearchOption.AllDirectories)
                .Where(p => MacroExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();

    /// <summary>Path for a macro name; keeps a .js/.iim extension, otherwise adds <paramref name="defaultExtension"/>.</summary>
    public string PathFor(string name, string defaultExtension = ".iim")
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (!MacroExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) name += defaultExtension;
        return Path.Combine(MacrosFolder, name);
    }
}
