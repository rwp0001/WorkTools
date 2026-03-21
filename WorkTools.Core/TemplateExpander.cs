using System.Text;

namespace WorkTools.Core;

public static class TemplateExpander
{
    private const string Placeholder = "{{TagName}}";

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

    public static List<string> ValidateTemplate(string templateText)
    {
        var errors = new List<string>();
        string[] templateLines = templateText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var sections = ParseTemplate(templateLines);

        if (sections.Count == 0)
        {
            errors.Add("No valid sections found. Each section must start with a header in [brackets].");
            return errors;
        }

        var headerPattern = new System.Text.RegularExpressions.Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        foreach (var section in sections)
        {
            if (!headerPattern.IsMatch(section.SectionHeader))
            {
                errors.Add($"Section header '{section.SectionHeader}' does not match expected format [Datatype.N.N] where Datatype is Numeric, Flag, or String.");
            }

            int expectedFieldCount = section.ColumnHeader.Split('\t').Length;

            for (int d = 0; d < section.DataRows.Count; d++)
            {
                int actualFieldCount = section.DataRows[d].Split('\t').Length;
                if (actualFieldCount != expectedFieldCount)
                {
                    errors.Add($"Section '{section.SectionHeader}' row {d + 1}: has {actualFieldCount} field(s) but header has {expectedFieldCount}.");
                }
            }
        }

        return errors;
    }

    public static List<(string SectionHeader, string Content)> GeneratePreviewBySections(string templateText, string tagsText)
    {
        string[] tagNames = ParseTagNames(tagsText);
        var sections = ParseTemplateSections(templateText);

        var results = new (string SectionHeader, string Content)[sections.Count];

        Parallel.For(0, sections.Count, i =>
        {
            results[i] = ExpandSection(sections[i], tagNames);
        });

        return results.ToList();
    }

    public static (string Header, string Content) ExpandSection(Section section, string[] tagNames)
    {
        // Pre-split each data row on the placeholder so we scan each row only once
        var splitRows = new string[section.DataRows.Count][];
        int totalSegmentLength = 0;
        for (int r = 0; r < section.DataRows.Count; r++)
        {
            splitRows[r] = section.DataRows[r].Split(Placeholder);
            foreach (var seg in splitRows[r])
                totalSegmentLength += seg.Length;
        }

        // Estimate capacity: column header + newline, then per tag per row:
        // segment chars + (splits-1)*tagName.Length + newline
        int avgTagLen = tagNames.Length > 0 ? tagNames[0].Length : 0;
        int rowCount = section.DataRows.Count;
        int capacity = section.ColumnHeader.Length + 2
            + tagNames.Length * (totalSegmentLength + rowCount * (avgTagLen + 2));

        var sb = new StringBuilder(capacity);
        sb.AppendLine(section.ColumnHeader);

        foreach (string tagName in tagNames)
        {
            foreach (var parts in splitRows)
            {
                sb.Append(parts[0]);
                for (int p = 1; p < parts.Length; p++)
                {
                    sb.Append(tagName);
                    sb.Append(parts[p]);
                }
                sb.AppendLine();
            }
        }

        string header = section.SectionHeader.Trim();
        if (header.StartsWith('[') && header.EndsWith(']'))
            header = header[1..^1];

        return (header, sb.ToString());
    }

    public static string[] ParseTagNames(string tagsText)
    {
        return tagsText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    public static List<Section> ParseTemplateSections(string templateText)
    {
        string[] templateLines = templateText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        return ParseTemplate(templateLines);
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

            // Pre-split rows on placeholder once per section
            var splitRows = new string[section.DataRows.Count][];
            for (int r = 0; r < section.DataRows.Count; r++)
                splitRows[r] = section.DataRows[r].Split(Placeholder);

            foreach (string tagName in tagNames)
            {
                foreach (var parts in splitRows)
                {
                    // Assemble from pre-split segments
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (p > 0)
                            writer.Write(tagName);
                        writer.Write(parts[p]);
                    }
                    writer.WriteLine();
                }
            }
        }
    }
}
