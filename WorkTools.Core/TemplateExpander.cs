using System.Text;
using System.Text.RegularExpressions;

namespace WorkTools.Core;

public static class TemplateExpander
{
    private const string Placeholder = "{{TagName}}";
    private const string SecAccessColumnName = "Sec / Access";
    private const string PersistColumnName = "Persist";
    private const string AccessColumnName = "Access";
    private const string SecLoggingColumnName = "Sec / Logging";
    private const string SecAccessExamples = "Examples: 'M¬R1', 'R2¬P with CBO', 'Authenticated Users'.";
    private static readonly HashSet<string> AllowedSecAccessLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default for Object",
        "No Access",
        "Authenticated Users",
        "Unauthenticated Users"
    };
    private static readonly HashSet<string> AllowedPersistValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Non-Retentive",
        "Retentive"
    };
    // Access field options from Crimson export.
    private static readonly HashSet<string> AllowedAccessValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Read and Write",
        "Write Only",
        "Read Only"
    };
    // Sec / Logging (Write Logging) field options from Crimson export.
    private static readonly HashSet<string> AllowedSecLoggingValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default for Object",
        "Do Not Log Changes",
        "Log Changes by Users",
        "Log Changes by Users and Programs"
    };
    private static readonly HashSet<string> AllowedSecAccessRoleTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "M", "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "P"
    };
    private static readonly string[] RequiredTagFields =
    [
        "Name",
        "Value",
        "Label",
        "Alias",
        "Desc",
        "Class",
        "Sec / Access",
        "Sec / Logging"
    ];
    private static readonly HashSet<string> RequiredNonBlankFieldSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "Value",
        "Sec / Access",
        "Sec / Logging"
    };
    private static readonly Regex SectionHeaderPattern = new(@"^\[(Numeric|Flag|String)\.(\d)\.(\d)\]$", RegexOptions.Compiled);

    // In Crimson section headers [Datatype.X.Y], X is Format Type.
    private static readonly Dictionary<int, HashSet<string>> AllowedDatatypesByFormatType = new()
    {
        [0] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric", "String" },
        [1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric" },
        [2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric" },
        [3] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric" },
        [4] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric" },
        [5] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Flag", "Numeric" },
        [6] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric" },
        [7] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric", "String" },
        [8] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "String" }
    };

    // In Crimson section headers [Datatype.X.Y], Y is Color Type:
    // 0 = General, 1 = Fixed, 2 = Two-State, 3 = Multi-State, 4 = Linked.
    private static readonly Dictionary<int, string> ColorTypeNames = new()
    {
        [0] = "General",
        [1] = "Fixed",
        [2] = "Two-State",
        [3] = "Multi-State",
        [4] = "Linked"
    };

    // Color Type compatibility by datatype:
    // General(0): Flag, Numeric, String
    // Fixed(1): Flag, Numeric, String
    // Two-State(2): Flag, Numeric
    // Multi-State(3): Numeric
    // Linked(4): Flag, Numeric, String
    private static readonly Dictionary<int, HashSet<string>> AllowedDatatypesByColorType = new()
    {
        [0] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Flag", "Numeric", "String" },
        [1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Flag", "Numeric", "String" },
        [2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Flag", "Numeric" },
        [3] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Numeric" },
        [4] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Flag", "Numeric", "String" }
    };

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
        string[] tagNames = ParseTagNames(File.ReadAllText(tagsPath));

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
        string[] templateLines = SplitLines(templateText);
        string[] tagNames = ParseTagNames(tagsText);

        var sections = ParseTemplate(templateLines);

        using var writer = new StringWriter();
        WriteSections(sections, tagNames, writer);
        return writer.ToString();
    }

    public static (int TagCount, int ReplacementCount, int OutputLineCount) GetStats(string templateText, string tagsText)
    {
        string[] templateLines = SplitLines(templateText);
        string[] tagNames = ParseTagNames(tagsText);

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
        string[] templateLines = SplitLines(templateText);
        var sections = ParseTemplate(templateLines);

        if (sections.Count == 0)
        {
            errors.Add("No valid sections found. Each section must start with a header in [brackets].");
            return errors;
        }

        foreach (var section in sections)
        {
            var match = SectionHeaderPattern.Match(section.SectionHeader);
            if (!match.Success)
            {
                errors.Add($"Section header '{section.SectionHeader}' does not match expected format [Datatype.N.N] where Datatype is Numeric, Flag, or String.");
            }
            else
            {
                string datatype = match.Groups[1].Value;
                int formatType = int.Parse(match.Groups[2].Value);
                int colorType = int.Parse(match.Groups[3].Value);

                if (!AllowedDatatypesByFormatType.TryGetValue(formatType, out var allowedDatatypes))
                {
                    errors.Add($"Section '{section.SectionHeader}' uses unsupported Format Type '{formatType}'.");
                }
                else if (!allowedDatatypes.Contains(datatype))
                {
                    string allowed = string.Join(", ", allowedDatatypes.OrderBy(v => v, StringComparer.Ordinal));
                    errors.Add($"Section '{section.SectionHeader}' has datatype '{datatype}' but Format Type '{formatType}' allows: {allowed}.");
                }

                bool hasColorTypeName = ColorTypeNames.TryGetValue(colorType, out string? colorTypeName);
                string colorTypeDisplay = hasColorTypeName ? $"{colorType} ({colorTypeName})" : colorType.ToString();

                if (!AllowedDatatypesByColorType.TryGetValue(colorType, out var allowedColorDatatypes))
                {
                    errors.Add($"Section '{section.SectionHeader}' uses unsupported Color Type '{colorTypeDisplay}'.");
                }
                else if (!allowedColorDatatypes.Contains(datatype))
                {
                    string allowed = string.Join(", ", allowedColorDatatypes.OrderBy(v => v, StringComparer.Ordinal));
                    errors.Add($"Section '{section.SectionHeader}' has datatype '{datatype}' but Color Type '{colorTypeDisplay}' allows: {allowed}.");
                }
            }

            string[] headers = GetNormalizedHeaders(section.ColumnHeader);
            int expectedFieldCount = headers.Length;
            var columnSet = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            int secAccessColumnIndex = FindColumnIndex(headers, SecAccessColumnName);
            int persistColumnIndex = FindColumnIndex(headers, PersistColumnName);
            int accessColumnIndex = FindColumnIndex(headers, AccessColumnName);
            int secLoggingColumnIndex = FindColumnIndex(headers, SecLoggingColumnName);

            var missingFields = RequiredTagFields
                .Where(field => !columnSet.Contains(field))
                .ToArray();

            if (missingFields.Length > 0)
            {
                errors.Add(
                    $"Section '{section.SectionHeader}' is missing required tag field(s): {string.Join(", ", missingFields)}.");
            }

            for (int d = 0; d < section.DataRows.Count; d++)
            {
                string[] fields = SplitTabDelimited(section.DataRows[d]);
                int actualFieldCount = fields.Length;
                if (actualFieldCount != expectedFieldCount)
                {
                    errors.Add($"Section '{section.SectionHeader}' row {d + 1}: has {actualFieldCount} field(s) but header has {expectedFieldCount}.");
                    continue;
                }

                for (int i = 0; i < headers.Length; i++)
                {
                    if (RequiredNonBlankFieldSet.Contains(headers[i]) && string.IsNullOrWhiteSpace(fields[i]))
                    {
                        errors.Add($"Section '{section.SectionHeader}' row {d + 1}: required field '{headers[i]}' cannot be blank.");
                    }
                }

                if (secAccessColumnIndex >= 0)
                {
                    string secAccessValue = fields[secAccessColumnIndex];
                    if (!TryValidateSecAccess(secAccessValue, out string? secAccessError))
                    {
                        errors.Add($"Section '{section.SectionHeader}' row {d + 1}: invalid '{SecAccessColumnName}' value '{secAccessValue}'. {secAccessError}");
                    }
                }

                if (persistColumnIndex >= 0)
                {
                    string persistValue = fields[persistColumnIndex].Trim();
                    if (!string.IsNullOrWhiteSpace(persistValue) && !AllowedPersistValues.Contains(persistValue))
                    {
                        errors.Add($"Section '{section.SectionHeader}' row {d + 1}: invalid '{PersistColumnName}' value '{fields[persistColumnIndex]}'. Allowed values: Non-Retentive, Retentive.");
                    }
                }

                if (accessColumnIndex >= 0)
                {
                    string accessValue = fields[accessColumnIndex].Trim();
                    if (!string.IsNullOrWhiteSpace(accessValue) && !AllowedAccessValues.Contains(accessValue))
                    {
                        errors.Add($"Section '{section.SectionHeader}' row {d + 1}: invalid '{AccessColumnName}' value '{fields[accessColumnIndex]}'. Allowed values: Read and Write, Write Only, Read Only.");
                    }
                }

                if (secLoggingColumnIndex >= 0)
                {
                    string secLoggingValue = fields[secLoggingColumnIndex].Trim();
                    if (!string.IsNullOrWhiteSpace(secLoggingValue) && !AllowedSecLoggingValues.Contains(secLoggingValue))
                    {
                        errors.Add($"Section '{section.SectionHeader}' row {d + 1}: invalid '{SecLoggingColumnName}' value '{fields[secLoggingColumnIndex]}'. Allowed values: Default for Object, Do Not Log Changes, Log Changes by Users, Log Changes by Users and Programs.");
                    }
                }
            }
        }

        return errors;
    }

    private static bool TryValidateSecAccess(string value, out string? error)
    {
        string trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Value cannot be blank.";
            return false;
        }

        if (AllowedSecAccessLiterals.Contains(trimmed))
        {
            error = null;
            return true;
        }

        const string withCboSuffix = " with CBO";
        bool hasWithCbo = trimmed.EndsWith(withCboSuffix, StringComparison.OrdinalIgnoreCase);
        string coreValue = hasWithCbo
            ? trimmed[..^withCboSuffix.Length].TrimEnd()
            : trimmed;

        string[] tokens = coreValue
            .Split('\u00AC', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        if (tokens.Length == 0)
        {
            error = $"Expected one or more access tokens (M, R1-R8, P), optionally followed by ' with CBO'. {SecAccessExamples}";
            return false;
        }

        var invalidTokens = tokens
            .Where(t => !AllowedSecAccessRoleTokens.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalidTokens.Length > 0)
        {
            error = $"Invalid access token(s): {string.Join(", ", invalidTokens)}. Allowed tokens: M, R1-R8, P. {SecAccessExamples}";
            return false;
        }

        var duplicateTokens = tokens
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicateTokens.Length > 0)
        {
            error = $"Duplicate access token(s): {string.Join(", ", duplicateTokens)}. Each token may only appear once. {SecAccessExamples}";
            return false;
        }

        error = null;
        return true;
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

    public static string[] ParseTagNames(string tagsText, bool sort = false)
    {
        return NormalizeTagNames(SplitLines(tagsText), sort: sort);
    }

    public static string[] ParseCrimsonExportTagNames(string exportText, string nameColumn = "Name", bool sort = false)
    {
        if (string.IsNullOrWhiteSpace(exportText))
            return [];

        var sections = ParseTemplateSections(exportText);
        if (sections.Count == 0)
            return [];

        var tags = new List<string>();

        foreach (var section in sections)
        {
            string[] headers = GetNormalizedHeaders(section.ColumnHeader);
            int nameIndex = FindColumnIndex(headers, nameColumn);

            if (nameIndex < 0)
                continue;

            foreach (string row in section.DataRows)
            {
                string[] fields = SplitTabDelimited(row);
                if (nameIndex >= fields.Length)
                    continue;

                string tagName = fields[nameIndex].Trim();
                if (!string.IsNullOrWhiteSpace(tagName))
                    tags.Add(tagName);
            }
        }

        return NormalizeTagNames(tags, deduplicate: true, sort: sort);
    }

    public static string BuildTagListFromCrimsonExport(string exportText, string nameColumn = "Name", bool sort = false)
    {
        string[] tags = ParseCrimsonExportTagNames(exportText, nameColumn, sort);
        return string.Join(Environment.NewLine, tags);
    }

    public static string BuildTagListFromCrimsonExportFile(string exportFilePath, string nameColumn = "Name", bool sort = false)
    {
        using var reader = new StreamReader(exportFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return BuildTagListFromCrimsonExport(text, nameColumn, sort);
    }

    public static List<Section> ParseTemplateSections(string templateText)
    {
        return ParseTemplate(SplitLines(templateText));
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

    private static string[] SplitLines(string text)
    {
        return text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
    }

    private static string[] SplitTabDelimited(string value)
    {
        return value.Split('\t');
    }

    private static string[] GetNormalizedHeaders(string columnHeader)
    {
        return SplitTabDelimited(columnHeader)
            .Select(h => h.Trim())
            .ToArray();
    }

    private static int FindColumnIndex(string[] headers, string columnName)
    {
        return Array.FindIndex(headers,
            h => string.Equals(h, columnName, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeTagNames(IEnumerable<string> tagNames, bool deduplicate = false, bool sort = false)
    {
        IEnumerable<string> normalized = tagNames
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag));

        if (deduplicate)
            normalized = normalized.Distinct(StringComparer.OrdinalIgnoreCase);

        if (sort)
            normalized = normalized.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);

        return normalized.ToArray();
    }
}
