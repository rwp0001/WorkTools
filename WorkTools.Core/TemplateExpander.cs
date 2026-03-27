using System.Text;

namespace WorkTools.Core;

public static class TemplateExpander
{
    private const string Placeholder = "{{TagName}}";
    private const string SecAccessColumnName = "Sec / Access";
    private const string PersistColumnName = "Persist";
    private const string AccessColumnName = "Access";
    private const string SecLoggingColumnName = "Sec / Logging";
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
    private static readonly string[] RequiredNonBlankFields =
    [
        "Name",
        "Value",
        "Sec / Access",
        "Sec / Logging"
    ];

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

        var headerPattern = new System.Text.RegularExpressions.Regex(@"^\[(Numeric|Flag|String)\.(\d)\.(\d)\]$");

        foreach (var section in sections)
        {
            var match = headerPattern.Match(section.SectionHeader);
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

            int expectedFieldCount = section.ColumnHeader.Split('\t').Length;
            string[] headers = section.ColumnHeader.Split('\t');
            var columnSet = headers
                .Select(c => c.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            int secAccessColumnIndex = Array.FindIndex(
                headers,
                h => string.Equals(h.Trim(), SecAccessColumnName, StringComparison.OrdinalIgnoreCase));
            int persistColumnIndex = Array.FindIndex(
                headers,
                h => string.Equals(h.Trim(), PersistColumnName, StringComparison.OrdinalIgnoreCase));
            int accessColumnIndex = Array.FindIndex(
                headers,
                h => string.Equals(h.Trim(), AccessColumnName, StringComparison.OrdinalIgnoreCase));
            int secLoggingColumnIndex = Array.FindIndex(
                headers,
                h => string.Equals(h.Trim(), SecLoggingColumnName, StringComparison.OrdinalIgnoreCase));

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
                string[] fields = section.DataRows[d].Split('\t');
                int actualFieldCount = fields.Length;
                if (actualFieldCount != expectedFieldCount)
                {
                    errors.Add($"Section '{section.SectionHeader}' row {d + 1}: has {actualFieldCount} field(s) but header has {expectedFieldCount}.");
                    continue;
                }

                // Check that required non-blank fields are actually non-blank
                for (int i = 0; i < headers.Length; i++)
                {
                    string headerTrimmed = headers[i].Trim();
                    if (RequiredNonBlankFields.Contains(headerTrimmed) && string.IsNullOrWhiteSpace(fields[i]))
                    {
                        errors.Add($"Section '{section.SectionHeader}' row {d + 1}: required field '{headerTrimmed}' cannot be blank.");
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

    private static List<string> ValidateNonBlankFields(string[] fields, string sectionHeader, int rowNumber)
    {
        var blankFieldErrors = new List<string>();

        for (int i = 0; i < fields.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(fields[i]))
            {
                string fieldName = i < RequiredTagFields.Length ? RequiredTagFields[i] : $"Field{i + 1}";
                if (RequiredNonBlankFields.Contains(fieldName))
                {
                    blankFieldErrors.Add($"Section '{sectionHeader}' row {rowNumber}: '{fieldName}' cannot be blank.");
                }
            }
        }

        return blankFieldErrors;
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
            error = "Expected one or more access tokens (M, R1-R8, P), optionally followed by ' with CBO'.";
            return false;
        }

        var invalidTokens = tokens
            .Where(t => !AllowedSecAccessRoleTokens.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalidTokens.Length > 0)
        {
            error = $"Invalid access token(s): {string.Join(", ", invalidTokens)}.";
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

    public static string[] ParseTagNames(string tagsText)
    {
        return tagsText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    public static string[] ParseCrimsonExportTagNames(string exportText, string nameColumn = "Name")
    {
        if (string.IsNullOrWhiteSpace(exportText))
            return [];

        var sections = ParseTemplateSections(exportText);
        if (sections.Count == 0)
            return [];

        var tags = new List<string>();

        foreach (var section in sections)
        {
            string[] headers = section.ColumnHeader.Split('\t');
            int nameIndex = Array.FindIndex(headers,
                h => string.Equals(h.Trim(), nameColumn, StringComparison.OrdinalIgnoreCase));

            if (nameIndex < 0)
                continue;

            foreach (string row in section.DataRows)
            {
                string[] fields = row.Split('\t');
                if (nameIndex >= fields.Length)
                    continue;

                string tagName = fields[nameIndex].Trim();
                if (!string.IsNullOrWhiteSpace(tagName))
                    tags.Add(tagName);
            }
        }

        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string BuildTagListFromCrimsonExport(string exportText, string nameColumn = "Name")
    {
        string[] tags = ParseCrimsonExportTagNames(exportText, nameColumn);
        return string.Join(Environment.NewLine, tags);
    }

    public static string BuildTagListFromCrimsonExportFile(string exportFilePath, string nameColumn = "Name")
    {
        using var reader = new StreamReader(exportFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return BuildTagListFromCrimsonExport(text, nameColumn);
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
