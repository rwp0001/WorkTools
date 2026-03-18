string templatePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Template.txt");
string tagsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TagsList.txt");
string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Output.txt");

string[] templateLines = File.ReadAllLines(templatePath);
string[] tagNames = File.ReadAllLines(tagsPath)
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .ToArray();

// Parse the template into sections: each section has a header, a column header, and data rows.
var sections = new List<(string SectionHeader, string ColumnHeader, List<string> DataRows)>();

for (int i = 0; i < templateLines.Length; i++)
{
    string line = templateLines[i];
    if (line.StartsWith('[') && line.Trim().EndsWith(']'))
    {
        string sectionHeader = line;
        string? columnHeader = null;
        var dataRows = new List<string>();

        // Skip blank lines after section header, then first non-blank line is the column header
        for (i++; i < templateLines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(templateLines[i]))
                continue;

            if (templateLines[i].Trim().StartsWith('['))
            {
                i--; // re-process this line as a new section
                break;
            }

            if (columnHeader is null)
                columnHeader = templateLines[i];
            else
                dataRows.Add(templateLines[i]);
        }

        if (columnHeader is not null)
            sections.Add((sectionHeader, columnHeader, dataRows));
    }
}

// Write output with merged sections
using var writer = new StreamWriter(outputPath);
writer.WriteLine();

for (int s = 0; s < sections.Count; s++)
{
    var (sectionHeader, columnHeader, dataRows) = sections[s];

    if (s > 0)
        writer.WriteLine();

    writer.WriteLine(sectionHeader);
    writer.WriteLine();
    writer.WriteLine(columnHeader);

    foreach (string tagName in tagNames)
    {
        foreach (string row in dataRows)
        {
            writer.WriteLine(row.Replace("{{TagName}}", tagName));
        }
    }
}

Console.WriteLine($"Generated output for {tagNames.Length} tags across {sections.Count} sections -> {Path.GetFullPath(outputPath)}");
