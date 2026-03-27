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
    private const string FormTypeColumnName = "FormType";
    private const string ColTypeColumnName = "ColType";
    private const string FormatLinkColumnName = "Format / Link";
    private const string ColorLinkColumnName = "Color / Link";
    private const string ColorColorColumnName = "Color / Color";
    private const string ColorOnColumnName = "Color / On";
    private const string ColorOffColumnName = "Color / Off";
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
    // Accepted Access values used by this validator for Crimson export/import data.
    private static readonly HashSet<string> AllowedAccessValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Read and Write",
        "Write Only",
        "Read Only"
    };
    // Accepted Sec / Logging values used by this validator for Crimson export/import data.
    private static readonly HashSet<string> AllowedSecLoggingValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default for Object",
        "Do Not Log Changes",
        "Log Changes by Users",
        "Log Changes by Users and Programs"
    };
    // Accepted FormType values used by this validator for non-simple Crimson tags.
    private static readonly HashSet<string> AllowedFormTypeValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "General",
        "Numeric",
        "Scientific",
        "Time and Date",
        "IP Address",
        "Two-State",
        "Multi-State",
        "Linked",
        "String"
    };
    private static readonly Dictionary<string, HashSet<string>> AllowedFormTypesByDatatype = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Numeric"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "General", "Numeric", "Scientific", "Time and Date", "IP Address", "Two-State", "Multi-State", "Linked"
        },
        ["Flag"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Two-State", "Linked" },
        ["String"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "General", "Linked", "String" }
    };
    // Accepted ColType values used by this validator for non-simple Crimson tags.
    private static readonly HashSet<string> AllowedColTypeValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "General",
        "Fixed",
        "Two-State",
        "Multi-State",
        "Linked"
    };
    private static readonly Dictionary<string, HashSet<string>> AllowedColTypesByDatatype = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Numeric"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "General", "Fixed", "Two-State", "Multi-State", "Linked" },
        ["Flag"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "General", "Fixed", "Two-State", "Linked" },
        ["String"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "General", "Fixed", "Linked" }
    };
    private static readonly HashSet<string> AllowedSecAccessRoleTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "M", "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "P"
    };
    // Minimum columns this validator requires for the non-simple expandable template workflow.
    private static readonly string[] RequiredTagFields =
    [
        "Name",
        "Value",
        "Label",
        "Alias",
        "Desc",
        "Class",
        "FormType",
        "ColType",
        "Sec / Access",
        "Sec / Logging"
    ];
    // Required columns above that must also contain a non-blank value in each data row.
    private static readonly HashSet<string> RequiredNonBlankFieldSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "Value",
        "FormType",
        "ColType",
        "Sec / Access",
        "Sec / Logging"
    };
    // Simple tags are valid in Crimson, but this template expansion workflow only recognizes
    // the expandable section types used here: Numeric, Flag, and String.
    private static readonly Regex SectionHeaderPattern = new(@"^\[(Numeric|Flag|String)\.(\d)\.(\d)\]$", RegexOptions.Compiled);

    // In a Crimson section header like [Numeric.X.Y], X is the format type code:
    // 0 = General, 1 = Numeric, 2 = Scientific, 3 = Time and Date, 4 = IP Address,
    // 5 = Two-State, 6 = Multi-State, 7 = Linked, 8 = String.
    private static readonly Dictionary<int, string> FormatTypeNames = new()
    {
        [0] = "General",
        [1] = "Numeric",
        [2] = "Scientific",
        [3] = "Time and Date",
        [4] = "IP Address",
        [5] = "Two-State",
        [6] = "Multi-State",
        [7] = "Linked",
        [8] = "String"
    };

    // In a Crimson section header like [Numeric.X.Y], X is the format type code.
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

    // In a Crimson section header like [Numeric.X.Y], Y is the color type code:
    // 0 = General, 1 = Fixed, 2 = Two-State, 3 = Multi-State, 4 = Linked.
    private static readonly Dictionary<int, string> ColorTypeNames = new()
    {
        [0] = "General",
        [1] = "Fixed",
        [2] = "Two-State",
        [3] = "Multi-State",
        [4] = "Linked"
    };

    // Allowed color type compatibility by datatype:
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

        // Header lines per section: section header + blank line + column header = 3,
        // plus the leading blank line and blank separators between sections.
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
            string? sectionDatatype = null;
            string? sectionFormatTypeName = null;
            string? sectionColorTypeName = null;

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
                sectionDatatype = datatype;

                bool hasFormatTypeName = FormatTypeNames.TryGetValue(formatType, out string? formatTypeName);
                string formatTypeDisplay = hasFormatTypeName ? $"{formatType} ({formatTypeName})" : formatType.ToString();
                sectionFormatTypeName = formatTypeName;

                if (!AllowedDatatypesByFormatType.TryGetValue(formatType, out var allowedDatatypes))
                {
                    errors.Add($"Section '{section.SectionHeader}' uses unsupported Format Type '{formatTypeDisplay}'.");
                }
                else if (!allowedDatatypes.Contains(datatype))
                {
                    string allowed = string.Join(", ", allowedDatatypes.OrderBy(v => v, StringComparer.Ordinal));
                    errors.Add($"Section '{section.SectionHeader}' has datatype '{datatype}' but Format Type '{formatTypeDisplay}' allows: {allowed}.");
                }

                bool hasColorTypeName = ColorTypeNames.TryGetValue(colorType, out string? colorTypeName);
                string colorTypeDisplay = hasColorTypeName ? $"{colorType} ({colorTypeName})" : colorType.ToString();
                sectionColorTypeName = colorTypeName;

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
            int formTypeColumnIndex = FindColumnIndex(headers, FormTypeColumnName);
            int colTypeColumnIndex = FindColumnIndex(headers, ColTypeColumnName);

            var missingFields = RequiredTagFields
                .Where(field => !columnSet.Contains(field))
                .ToArray();

            if (missingFields.Length > 0)
            {
                errors.Add(
                    $"Section '{section.SectionHeader}' is missing required tag field(s): {string.Join(", ", missingFields)}.");
            }

            if (sectionFormatTypeName is not null)
            {
                foreach (string requiredFormatColumn in GetRequiredFormatColumns(sectionFormatTypeName))
                {
                    if (!columnSet.Contains(requiredFormatColumn))
                    {
                        errors.Add($"Section '{section.SectionHeader}' uses FormType '{sectionFormatTypeName}' and requires column '{requiredFormatColumn}'. ");
                    }
                }
            }

            if (sectionColorTypeName is not null)
            {
                foreach (string requiredColorColumn in GetRequiredColorColumns(sectionColorTypeName))
                {
                    if (!columnSet.Contains(requiredColorColumn))
                    {
                        errors.Add($"Section '{section.SectionHeader}' uses ColType '{sectionColorTypeName}' and requires column '{requiredColorColumn}'. ");
                    }
                }
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

                if (formTypeColumnIndex >= 0)
                {
                    string formTypeValue = fields[formTypeColumnIndex].Trim();
                    if (!string.IsNullOrWhiteSpace(formTypeValue))
                    {
                        if (!AllowedFormTypeValues.Contains(formTypeValue))
                        {
                            errors.Add($"Section '{section.SectionHeader}' row {d + 1}: invalid '{FormTypeColumnName}' value '{fields[formTypeColumnIndex]}'. Allowed values: General, Numeric, Scientific, Time and Date, IP Address, Two-State, Multi-State, Linked, String.");
                        }
                        else
                        {
                            if (sectionDatatype is not null
                                && AllowedFormTypesByDatatype.TryGetValue(sectionDatatype, out var allowedFormTypes)
                                && !allowedFormTypes.Contains(formTypeValue))
                            {
                                string allowed = string.Join(", ", allowedFormTypes.OrderBy(v => v, StringComparer.Ordinal));
                                errors.Add($"Section '{section.SectionHeader}' row {d + 1}: datatype '{sectionDatatype}' does not allow '{FormTypeColumnName}' value '{formTypeValue}'. Allowed values: {allowed}.");
                            }

                            if (sectionFormatTypeName is not null && !string.Equals(formTypeValue, sectionFormatTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                errors.Add($"Section '{section.SectionHeader}' row {d + 1}: '{FormTypeColumnName}' value '{formTypeValue}' does not match section Format Type '{sectionFormatTypeName}'.");
                            }

                            foreach (string requiredFormatColumn in GetRequiredFormatColumns(formTypeValue))
                            {
                                int requiredFormatColumnIndex = FindColumnIndex(headers, requiredFormatColumn);
                                if (requiredFormatColumnIndex >= 0 && string.IsNullOrWhiteSpace(fields[requiredFormatColumnIndex]))
                                {
                                    errors.Add($"Section '{section.SectionHeader}' row {d + 1}: required field '{requiredFormatColumn}' cannot be blank when '{FormTypeColumnName}' is '{formTypeValue}'.");
                                }
                            }
                        }
                    }
                }

                if (colTypeColumnIndex >= 0)
                {
                    string colTypeValue = fields[colTypeColumnIndex].Trim();
                    if (!string.IsNullOrWhiteSpace(colTypeValue))
                    {
                        if (!AllowedColTypeValues.Contains(colTypeValue))
                        {
                            errors.Add($"Section '{section.SectionHeader}' row {d + 1}: invalid '{ColTypeColumnName}' value '{fields[colTypeColumnIndex]}'. Allowed values: General, Fixed, Two-State, Multi-State, Linked.");
                        }
                        else
                        {
                            if (sectionDatatype is not null
                                && AllowedColTypesByDatatype.TryGetValue(sectionDatatype, out var allowedColTypes)
                                && !allowedColTypes.Contains(colTypeValue))
                            {
                                string allowed = string.Join(", ", allowedColTypes.OrderBy(v => v, StringComparer.Ordinal));
                                errors.Add($"Section '{section.SectionHeader}' row {d + 1}: datatype '{sectionDatatype}' does not allow '{ColTypeColumnName}' value '{colTypeValue}'. Allowed values: {allowed}.");
                            }

                            if (sectionColorTypeName is not null && !string.Equals(colTypeValue, sectionColorTypeName, StringComparison.OrdinalIgnoreCase))
                            {
                                errors.Add($"Section '{section.SectionHeader}' row {d + 1}: '{ColTypeColumnName}' value '{colTypeValue}' does not match section Color Type '{sectionColorTypeName}'.");
                            }

                            foreach (string requiredColorColumn in GetRequiredColorColumns(colTypeValue))
                            {
                                int requiredColorColumnIndex = FindColumnIndex(headers, requiredColorColumn);
                                if (requiredColorColumnIndex >= 0 && string.IsNullOrWhiteSpace(fields[requiredColorColumnIndex]))
                                {
                                    errors.Add($"Section '{section.SectionHeader}' row {d + 1}: required field '{requiredColorColumn}' cannot be blank when '{ColTypeColumnName}' is '{colTypeValue}'.");
                                }
                            }
                        }
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
        // Pre-split each data row on the placeholder so each row is scanned only once.
        var splitRows = new string[section.DataRows.Count][];
        int totalSegmentLength = 0;
        for (int r = 0; r < section.DataRows.Count; r++)
        {
            splitRows[r] = section.DataRows[r].Split(Placeholder);
            foreach (var seg in splitRows[r])
                totalSegmentLength += seg.Length;
        }

        // Rough capacity estimate: column header + newline, then per tag per row:
        // segment chars + inserted tag name chars + newline.
        // The estimate uses the first tag length as a simple approximation.
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

            // Pre-split rows on the placeholder once per section.
            var splitRows = new string[section.DataRows.Count][];
            for (int r = 0; r < section.DataRows.Count; r++)
                splitRows[r] = section.DataRows[r].Split(Placeholder);

            foreach (string tagName in tagNames)
            {
                foreach (var parts in splitRows)
                {
                    // Rebuild the row by inserting the current tag name between split segments.
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

    private static string[] GetRequiredFormatColumns(string formType)
    {
        if (string.Equals(formType, "Linked", StringComparison.OrdinalIgnoreCase))
            return [FormatLinkColumnName];

        return [];
    }

    private static string[] GetRequiredColorColumns(string colType)
    {
        if (string.Equals(colType, "Linked", StringComparison.OrdinalIgnoreCase))
            return [ColorLinkColumnName];

        if (string.Equals(colType, "Fixed", StringComparison.OrdinalIgnoreCase))
            return [ColorColorColumnName];

        if (string.Equals(colType, "Two-State", StringComparison.OrdinalIgnoreCase))
            return [ColorOnColumnName, ColorOffColumnName];

        return [];
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
