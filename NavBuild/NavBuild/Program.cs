using System.Text;
GenerateNavigation("E:\\JaydenAI");
static void GenerateNavigation(string directory)
{
    var navFile = Path.Combine(directory, "_data/navigation.yml");
    var dir = Path.GetDirectoryName(navFile);
    if (!Directory.Exists(dir))
    {
        Directory.CreateDirectory(dir!);
    }
    if (File.Exists(navFile))
    { 

        File.Delete(navFile);
    }

    var builder = new StringBuilder();
    foreach (var (g, d) in ParseGroup(directory))
    {
        builder.AppendLine($"- group_name: \"{g}\"");
        builder.AppendLine("  items:");
        var ordernal = 1;
        foreach (var file in Directory.GetFiles(d))
        {
            if (Path.GetExtension(file) != ".md")
            {
                continue;
            }
            var fileName = Path.GetFileName(file);
            var title = File.ReadLines(file).First().Trim();
            var index = title.IndexOf(']');
            if (index > -1)
            {
                title = title.Substring(index + 1).Trim();
            }
            title = title.Replace("\"", "");
            title = title.Replace("“", "");
            title = title.Replace("”", "");
            title = title.Replace("——", ":");
            builder.AppendLine($"    - title: \"{ordernal++:D2}.{title}\"");
            builder.AppendLine($"    - path: \"/{g}/{fileName}\"");
        }
        builder.AppendLine();
    }
    File.WriteAllText(navFile, builder.ToString() ,Encoding.UTF8);
}
static IEnumerable <(string GroupName, string Directory)>ParseGroup(string directory)
{
    return from subDirectory in Directory.GetDirectories(directory)
           let directoryName = new DirectoryInfo(subDirectory).Name
           where directoryName.Contains('.') && int.TryParse(directoryName.Split('.')[0], out _)
           select (directoryName, subDirectory);
}

