using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WorkTools.Core;

public static class L5kTemplateGenerator
{
    private const string ColumnHeader = "Name\tValue\tLabel\tAlias\tDesc\tClass\tFormType\tColType\tSec / Access\tSec / Logging";
    private static readonly HashSet<string> AtomicTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BOOL",
        "SINT",
        "INT",
        "DINT",
        "LINT",
        "USINT",
        "UINT",
        "UDINT",
        "ULINT",
        "REAL",
        "LREAL",
        "STRING"
    };

    private static readonly Regex NameTypePattern = new(
        @"^(?<name>[A-Za-z_][\w]*)\s*:\s*(?<type>[A-Za-z_][\w]*)(?<suffix>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UsagePattern = new(
        @"\bUsage\s*:=\s*(?<usage>Input|Output|InOut)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DirectionPattern = new(
        @"\b(?<usage>Input|Output|InOut)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DescriptionPattern = new(
        @"\b(?:Description|Desc)\s*:=\s*(?:""(?<dq>(?:[^""]|"""")*)""|'(?<sq>(?:[^']|'')*)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LocalStringInitPattern = new(
        @"^\s*:=\s*(?:""(?<dq>(?:[^""]|"""")*)""|'(?<sq>(?:[^']|'')*)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed record Parameter(string Name, string DataType, string Direction, string? Description = null, string? ValueOverride = null);

    public static IReadOnlyList<Parameter> ExtractAtomicParameters(string l5kText)
    {
        if (string.IsNullOrWhiteSpace(l5kText))
            return [];

        bool isL5x = IsLikelyL5x(l5kText);
        var parameters = (isL5x
                ? ExtractAtomicParametersFromL5x(l5kText)
                : ExtractAtomicParametersFromL5k(l5kText))
            .ToList();

        var localStringParameters = isL5x
            ? ExtractLocalStringParametersFromL5x(l5kText)
            : ExtractLocalStringParametersFromL5k(l5kText);

        var existingNames = parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var localString in localStringParameters)
        {
            if (existingNames.Add(localString.Name))
                parameters.Add(localString);
        }

        return parameters;
    }

    public static string GenerateCrimsonTemplate(string l5kText, string placeholder = "{{1}}")
    {
        var parameters = ExtractAtomicParameters(l5kText);
        if (parameters.Count == 0)
            throw new InvalidOperationException("No atomic Input/InOut/Output parameters were found in the L5K/L5X file.");

        var boolParameters = parameters.Where(p => p.DataType.Equals("BOOL", StringComparison.OrdinalIgnoreCase)).ToList();
        var stringParameters = parameters.Where(p => p.DataType.Equals("STRING", StringComparison.OrdinalIgnoreCase)).ToList();
        var numericParameters = parameters
            .Where(p => !p.DataType.Equals("BOOL", StringComparison.OrdinalIgnoreCase)
                        && !p.DataType.Equals("STRING", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sb = new StringBuilder();
        AppendSection(sb, "[Numeric.1.0]", "Numeric", numericParameters, placeholder);
        AppendSection(sb, "[Flag.5.0]", "Two-State", boolParameters, placeholder);
        AppendSection(sb, "[String.8.0]", "String", stringParameters, placeholder);

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string header, string formType, List<Parameter> parameters, string placeholder)
    {
        if (parameters.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine(header);
        sb.AppendLine();
        sb.AppendLine(ColumnHeader);

        foreach (var parameter in parameters)
        {
            string name = $"{placeholder}.{parameter.Name}";
            string value = parameter.ValueOverride ?? $"[{placeholder}.{parameter.Name}]";
            string fallbackDesc = $"{parameter.Direction} {parameter.DataType}";
            string desc = string.IsNullOrWhiteSpace(parameter.Description) ? fallbackDesc : parameter.Description;
            sb.Append(name).Append('\t')
                .Append(value).Append('\t')
                .Append('\t')
                .Append('\t')
                .Append(desc).Append('\t')
                .Append('\t')
                .Append(formType).Append('\t')
                .Append("General").Append('\t')
                .Append("Default for Object").Append('\t')
                .Append("Default for Object")
                .AppendLine();
        }
    }

    private static IReadOnlyList<Parameter> ExtractAtomicParametersFromL5k(string l5kText)
    {
        var parameters = new List<Parameter>();
        bool inParameterBlock = false;
        var pending = new StringBuilder();

        foreach (string rawLine in SplitLines(l5kText))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (!inParameterBlock)
            {
                if (line.Equals("PARAMETERS", StringComparison.OrdinalIgnoreCase))
                {
                    inParameterBlock = true;
                }

                continue;
            }

            if (line.StartsWith("END_PARAMETERS", StringComparison.OrdinalIgnoreCase))
            {
                TryAppendParameter(pending.ToString(), parameters);
                pending.Clear();
                inParameterBlock = false;
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (pending.Length > 0)
                pending.Append(' ');
            pending.Append(line);

            if (line.Contains(';'))
            {
                TryAppendParameter(pending.ToString(), parameters);
                pending.Clear();
            }
        }

        if (pending.Length > 0)
            TryAppendParameter(pending.ToString(), parameters);

        return parameters;
    }

    private static IReadOnlyList<Parameter> ExtractAtomicParametersFromL5x(string l5xText)
    {
        var parameters = new List<Parameter>();

        try
        {
            var document = XDocument.Parse(l5xText, LoadOptions.None);
            var parameterElements = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Parameter", StringComparison.OrdinalIgnoreCase));

            foreach (var parameterElement in parameterElements)
            {
                string name = (string?)parameterElement.Attribute("Name") ?? string.Empty;
                string dataType = (string?)parameterElement.Attribute("DataType") ?? string.Empty;
                string usage = (string?)parameterElement.Attribute("Usage") ?? string.Empty;
                string dimensions = (string?)parameterElement.Attribute("Dimensions")
                    ?? (string?)parameterElement.Attribute("Dimension")
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dataType) || string.IsNullOrWhiteSpace(usage))
                    continue;

                if (!AtomicTypes.Contains(dataType))
                    continue;

                if (!string.IsNullOrWhiteSpace(dimensions) || dataType.Contains('[', StringComparison.Ordinal) || name.Contains('[', StringComparison.Ordinal))
                    continue;

                string direction = NormalizeDirection(usage);
                string? description = TryGetDescriptionFromL5x(parameterElement);
                parameters.Add(new Parameter(name.Trim(), dataType.Trim(), direction, description));
            }
        }
        catch
        {
            return [];
        }

        return parameters;
    }

    private static bool IsLikelyL5x(string text)
    {
        string trimmed = text.TrimStart();
        if (!trimmed.StartsWith('<'))
            return false;

        return trimmed.Contains("<RSLogix5000Content", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<AddOnInstructionDefinition", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<Parameter", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryAppendParameter(string parameterText, List<Parameter> parameters)
    {
        string cleaned = parameterText.Trim();
        if (cleaned.Length == 0)
            return;

        if (cleaned.EndsWith(';'))
            cleaned = cleaned[..^1].TrimEnd();

        var match = NameTypePattern.Match(cleaned);
        if (!match.Success)
            return;

        string name = match.Groups["name"].Value;
        string dataType = match.Groups["type"].Value;
        string suffix = match.Groups["suffix"].Value;

        if (name.Contains('[', StringComparison.Ordinal) || dataType.Contains('[', StringComparison.Ordinal) || suffix.Contains('[', StringComparison.Ordinal))
            return;

        if (!AtomicTypes.Contains(dataType))
            return;

        string? direction = TryGetDirection(suffix);
        if (direction is null)
            return;

        string? description = TryGetDescriptionFromL5k(suffix);
        parameters.Add(new Parameter(name, dataType, direction, description));
    }

    private static string? TryGetDescriptionFromL5k(string text)
    {
        var match = DescriptionPattern.Match(text);
        if (!match.Success)
            return null;

        string quotedValue = match.Groups["dq"].Success
            ? match.Groups["dq"].Value.Replace("\"\"", "\"")
            : match.Groups["sq"].Value.Replace("''", "'");

        return SanitizeDescription(quotedValue);
    }

    private static string? TryGetDescriptionFromL5x(XElement parameterElement)
    {
        var descriptionElement = parameterElement.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase));

        if (descriptionElement is null)
            return null;

        return SanitizeDescription(descriptionElement.Value);
    }

    private static string? SanitizeDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string normalized = text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        return normalized.Length == 0 ? null : normalized;
    }

    private static string? TryGetDirection(string text)
    {
        var usageMatch = UsagePattern.Match(text);
        if (usageMatch.Success)
            return NormalizeDirection(usageMatch.Groups["usage"].Value);

        var directionMatch = DirectionPattern.Match(text);
        if (directionMatch.Success)
            return NormalizeDirection(directionMatch.Groups["usage"].Value);

        return null;
    }

    private static string NormalizeDirection(string value)
    {
        if (value.Equals("InOut", StringComparison.OrdinalIgnoreCase))
            return "InOut";
        if (value.Equals("Output", StringComparison.OrdinalIgnoreCase))
            return "Output";

        return "Input";
    }

    private static string[] SplitLines(string text)
    {
        return text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
    }

    private static IReadOnlyList<Parameter> ExtractLocalStringParametersFromL5k(string l5kText)
    {
        var parameters = new List<Parameter>();
        bool inLocalTagsBlock = false;
        var pending = new StringBuilder();

        foreach (string rawLine in SplitLines(l5kText))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (!inLocalTagsBlock)
            {
                if (line.Equals("LOCAL_TAGS", StringComparison.OrdinalIgnoreCase))
                    inLocalTagsBlock = true;

                continue;
            }

            if (line.StartsWith("END_LOCAL_TAGS", StringComparison.OrdinalIgnoreCase))
            {
                TryAppendLocalStringParameter(pending.ToString(), parameters);
                pending.Clear();
                inLocalTagsBlock = false;
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (pending.Length > 0)
                pending.Append(' ');
            pending.Append(line);

            if (line.Contains(';'))
            {
                TryAppendLocalStringParameter(pending.ToString(), parameters);
                pending.Clear();
            }
        }

        if (pending.Length > 0)
            TryAppendLocalStringParameter(pending.ToString(), parameters);

        return parameters;
    }

    private static IReadOnlyList<Parameter> ExtractLocalStringParametersFromL5x(string l5xText)
    {
        var parameters = new List<Parameter>();

        try
        {
            var document = XDocument.Parse(l5xText, LoadOptions.None);
            var localTagElements = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Tag", StringComparison.OrdinalIgnoreCase)
                    && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.Equals("LocalTags", StringComparison.OrdinalIgnoreCase)));

            foreach (var localTagElement in localTagElements)
            {
                string name = (string?)localTagElement.Attribute("Name") ?? string.Empty;
                string dataType = (string?)localTagElement.Attribute("DataType") ?? string.Empty;
                string dimensions = (string?)localTagElement.Attribute("Dimensions")
                    ?? (string?)localTagElement.Attribute("Dimension")
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) || !dataType.Equals("STRING", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(dimensions) || name.Contains('[', StringComparison.Ordinal))
                    continue;

                string initialValue = TryGetLocalStringValueFromL5x(localTagElement) ?? string.Empty;
                string valueOverride = BuildQuotedStringValue(initialValue);
                string? description = TryGetDescriptionFromL5x(localTagElement);

                parameters.Add(new Parameter(name.Trim(), "STRING", "Local", description, valueOverride));
            }
        }
        catch
        {
            return [];
        }

        return parameters;
    }

    private static void TryAppendLocalStringParameter(string localTagText, List<Parameter> parameters)
    {
        string cleaned = localTagText.Trim();
        if (cleaned.Length == 0)
            return;

        if (cleaned.EndsWith(';'))
            cleaned = cleaned[..^1].TrimEnd();

        var match = NameTypePattern.Match(cleaned);
        if (!match.Success)
            return;

        string name = match.Groups["name"].Value;
        string dataType = match.Groups["type"].Value;
        string suffix = match.Groups["suffix"].Value;

        if (!dataType.Equals("STRING", StringComparison.OrdinalIgnoreCase))
            return;

        if (name.Contains('[', StringComparison.Ordinal) || dataType.Contains('[', StringComparison.Ordinal) || suffix.Contains('[', StringComparison.Ordinal))
            return;

        string initialValue = TryGetLocalStringValueFromL5k(suffix) ?? string.Empty;
        string valueOverride = BuildQuotedStringValue(initialValue);
        string? description = TryGetDescriptionFromL5k(suffix);

        parameters.Add(new Parameter(name, "STRING", "Local", description, valueOverride));
    }

    private static string? TryGetLocalStringValueFromL5k(string text)
    {
        var match = LocalStringInitPattern.Match(text);
        if (!match.Success)
            return null;

        return match.Groups["dq"].Success
            ? match.Groups["dq"].Value.Replace("\"\"", "\"")
            : match.Groups["sq"].Value.Replace("''", "'");
    }

    private static string? TryGetLocalStringValueFromL5x(XElement localTagElement)
    {
        string? directValue = (string?)localTagElement.Attribute("Value");
        if (!string.IsNullOrEmpty(directValue))
            return directValue;

        var valueAttributeElement = localTagElement.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals("DataValue", StringComparison.OrdinalIgnoreCase)
                || element.Name.LocalName.Equals("Value", StringComparison.OrdinalIgnoreCase));

        string? attributeValue = (string?)valueAttributeElement?.Attribute("Value");
        if (!string.IsNullOrEmpty(attributeValue))
            return attributeValue;

        var dataElement = localTagElement.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Data", StringComparison.OrdinalIgnoreCase)
                || element.Name.LocalName.Equals("Value", StringComparison.OrdinalIgnoreCase));

        string? innerText = dataElement?.Value;
        if (!string.IsNullOrWhiteSpace(innerText))
            return innerText;

        return null;
    }

    private static string BuildQuotedStringValue(string text)
    {
        string normalized = text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private static int FindClosingQuote(string text, char quote, char escapeQuote)
    {
        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] != quote)
                continue;

            if (i + 1 < text.Length && text[i + 1] == escapeQuote)
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }
}
