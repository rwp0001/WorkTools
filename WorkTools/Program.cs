using WorkTools.Core;

string templatePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Template.txt");
string tagsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TagsList.txt");
string outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Output.txt");

TemplateExpander.Generate(templatePath, tagsPath, outputPath);

Console.WriteLine($"Generated output -> {Path.GetFullPath(outputPath)}");
