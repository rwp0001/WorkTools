namespace WorkTools.Core;

public static class TemplateExpander
{
    public record Section(string SectionHeader, string ColumnHeader, List<string> DataRows);

    public static List<Section> ParseTemplate(string[] templateLines)
    {
        var sections = new List<Section>();

        for (int i = 0; i < templateLines.Length; i++)
        {
            string line = templateLines[i];
            if (line.StartsWith('[') && line.Trim().EndsWith(']'))
            {
                string sectionHeader = line;
                string? columnHeader = null;
                var dataRows = new List<string>();

                for (i++; i < templateLines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(templateLines[i]))
                        continue;

                    if (templateLines[i].Trim().StartsWith('['))
                    {
                        i--;
                        break;
                    }

                    if (columnHeader is null)
                        columnHeader = templateLines[i];
                    else
                        dataRows.Add(templateLines[i]);
                }

                if (columnHeader is not null)
                    sections.Add(new Section(sectionHeader, columnHeader, dataRows));
            }
        }

        return sections;
    }

    public static void Generate(string templatePath, string tagsPath, string outputPath)
    {
        string[] templateLines = File.ReadAllLines(templatePath);
        string[] tagNames = File.ReadAllLines(tagsPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var sections = ParseTemplate(templateLines);

        using var writer = new StreamWriter(outputPath);
        WriteSections(sections, tagNames, writer);
    }

    public static void GenerateFromText(string templateText, string tagsText, string outputPath)
    {
        string result = GeneratePreview(templateText, tagsText);
        File.WriteAllText(outputPath, result);
    }

    public static string GeneratePreview(string templateText, string tagsText)
    {
        string[] templateLines = templateText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        string[] tagNames = tagsText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var sections = ParseTemplate(templateLines);

        using var writer = new StringWriter();
        WriteSections(sections, tagNames, writer);
        return writer.ToString();
    }

    public static (int TagCount, int ReplacementCount, int OutputLineCount) GetStats(string templateText, string tagsText)
    {
        string[] templateLines = templateText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        string[] tagNames = tagsText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var sections = ParseTemplate(templateLines);

        int totalDataRows = sections.Sum(s => s.DataRows.Count);
        int replacementCount = totalDataRows * tagNames.Length;

        // header lines per section: section header + blank line + column header = 3
        // plus leading blank line and separators between sections
        int outputLineCount = 1; // leading blank line
        for (int s = 0; s < sections.Count; s++)
        {
            if (s > 0)
                outputLineCount++; // separator blank line
            outputLineCount += 3; // section header + blank line + column header
            outputLineCount += sections[s].DataRows.Count * tagNames.Length;
        }

        return (tagNames.Length, replacementCount, outputLineCount);
    }

    public static List<(string SectionHeader, string Content)> GeneratePreviewBySections(string templateText, string tagsText)
    {
        string[] templateLines = templateText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        string[] tagNames = tagsText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var sections = ParseTemplate(templateLines);
        var results = new List<(string SectionHeader, string Content)>();

        foreach (var section in sections)
        {
            using var writer = new StringWriter();
            writer.WriteLine(section.ColumnHeader);
            foreach (string tagName in tagNames)
            {
                foreach (string row in section.DataRows)
                {
                    writer.WriteLine(row.Replace("{{TagName}}", tagName));
                }
            }

            string header = section.SectionHeader.Trim();
            if (header.StartsWith('[') && header.EndsWith(']'))
                header = header[1..^1];

            results.Add((header, writer.ToString()));
        }

        return results;
    }

    public static void WriteSections(List<Section> sections, string[] tagNames, TextWriter writer)
    {
        writer.WriteLine();

        for (int s = 0; s < sections.Count; s++)
        {
            var section = sections[s];

            if (s > 0)
                writer.WriteLine();

            writer.WriteLine(section.SectionHeader);
            writer.WriteLine();
            writer.WriteLine(section.ColumnHeader);

            foreach (string tagName in tagNames)
            {
                foreach (string row in section.DataRows)
                {
                    writer.WriteLine(row.Replace("{{TagName}}", tagName));
                }
            }
        }
    }
}
