# WorkTools repository instructions

This tool is used to create files to be imported into a HMI software called Crimson 3.2, it used to program Red Lion HMIs. When creating the template, the user makes the structure of the first instance of a class (A Rockwell Automation Studio 5000 AOI datatype normally) in the Crimson software. After the class is formatted the way they want, they export the tags to a file. The user gets a list of all the instances of the class from a Studio 5000 tag export. This data is used as inputs to this program to create all the tags the user needs in Crimson to create new HMI screens.

The Crimson software fails silently without changing the database when importing a malformed file. This creates issues for users. The goal of this program is to create files that will be imported without issues.

Crimson stores data in an object called a tag. This tag object contains information on how to acquire the value, format the value, colorize the value, and the security of the value.

Crimson has four datatypes for tags: Simple, Numeric, Flag, and String. Simple tags do not have formatting or color options. Flag tags are boolean values. Strings are tags that store a series of characters. Numeric tags are used for any value that is not a boolean or string.

The Crimson export file is in a TSV format for the data and plain text for section headers. A full section header is three lines: a blank line, the bracketed section header like [Datatype.#.#], and another blank line.

The header format is four three long:
1. The first line is blank.
2. The second line is the datatype enclosed in square brackets with two numbers separated by periods. The first number is the format type encoded as an integer: 
   - 0 General
   - 1 Numeric
   - 2 Scientific
   - 3 Time and Date
   - 4 IP Address
   - 5 Two-State
   - 6 Multi-State
   - 7 Linked
   - 8 String
3. The second number is the color type encoded as an integer: 
   - 0 General
   - 1 Fixed
   - 2 Two-State
   - 3 Multi-State
   - 4 Linked
4. The third line is blank.
5. The next line is the column headers in TSV format. The rest of the lines until a new section header is the tag data in TSV format.

## Tag Types

### Simple Tags
The Simple datatype header will always be "[Simple.0.0]". Simple tags are called "Basic" tags in the Crimson UI and are used to represent constants or expressions. The Simple datatype will only have the columns "Name", "Value", "Extent", "Sim", "Label", "Alias", "Desc", and "Class".

#### From the Data Tab of the Crimson UI

##### Data Source Section
- All tags have the column of "Name". Name is a unique value used to identify the tag. Periods are used to denote folder levels. The value can be accessed in Crimson using "Tag.Name". The column is required, the value is required.
- All tags have the column of "Value". Value is either the lateral value to be used, or the source to be used. If the value is enclosed in square brackets, the source is an external device such as a PLC. If the value is a lateral blank string, the value should be ((Empty)); otherwise, the string should be enclosed by quotes. The value can be accessed in Crimson using "Tag". The column is required, the value is required.

##### Data Simulation Section
##### Data Actions Section
##### Data Setpoint Section

#### From the Format Tab of the Crimson UI

##### Data Labels Section
- All tags have the column of "Label". The column is a string used by Crimson for the label shown next to a value. If it is blank, Crimson uses the Name of the tag. This value can be translated. The value can be accessed in Crimson using "Tag.Label". The column is required, the value is optional.
- All tags have the column of "Alias". If it is blank, Crimson displays the Name of the tag. The value can be used in Crimson to replace the tag name when communicating with a cloud connector. The column is required, the value is optional.
- All tags have the column of "Desc". This is a string used for a description of the tag. The value can be accessed in Crimson using "Tag.Desc". The column is required, the value is optional.
- All tags have the column of "Class". This is a string used for the class of the tag. Crimson does not use this value internally. The column is required, the value is optional.

##### Format Type Section
- The "FormType" column is used on all but simple tags. This column is used to define which "Format" prefixed columns will follow it. The valid values are "General", "Numeric", "Scientific", "Time and Date", "IP Address", "Two-State", "Multi-State", "Linked", "String". Flag types can only be "Two-State" or "Linked". String types can only be "General", "Linked", and "String". Numeric types may be any value except "String".
- The FormType of "General" will not have any "Format" prefixed columns.
- The FormType of "Linked" will have the next column be "Format / Link". The value will be a tag name, and Crimson will use its formatting when displaying this tag.
- The FormType of "Two-State" will have the next two columns be "Format / On" and "Format / Off". The values will be strings with the defaults of "ON" and "OFF". If the value is blank, Crimson will display the defaults.

##### Data Format Section

#### From the Colors Tab of the Crimson UI

##### Color Type Section
- The "ColType" column is used on all but simple tags. This column is used to define which "Color" prefixed columns will follow it. The valid values are "General", "Fixed", "Two-State", "Multi-State", "Linked". Flag types can only be "Two-State", "Linked", or "Two-State". String types can only be "General", "Linked", and "String".
- The colors may be encoded as a string containing color names such as "Blue" or "Red"; or a four-digit hex code (0xFFFF) representing a custom color. The data will be formatted as "Foreground Color on Background Color".
- The ColType of "General" will not have any "Color" prefixed columns.
- The ColType of "Linked" will have the next column be "Color / Link". The value will be a tag name, and Crimson will use its formatting when colorizing this tag.
- The ColType of "Fixed" will have the next column be "Color / Color". The value will use the color format above.
- The ColType of "Two-State" will have the next two columns be "Color / On" and "Color / Off". The values will use the color format above.

#### From the Alarms Tab of the Crimson UI

#### From the Triggers Tab of the Crimson UI

#### From the Plot Tab of the Crimson UI

#### From the Security Tab of the Crimson UI

##### Security Section
- The "Sec / Access" column is used on all but simple tags and is used to define the security for a tag. The value "Default for Object" is the default with no options set. The value "Authenticated Users" means the tag can only be written to when a user is logged on. The value "Unauthenticated Users" means the tag can only be written to when a user is not logged on. These values may be appended with the " and Programs" and/or " with CBO" options.
- Tags where the "Users with Specific Rights" option was selected in the Crimson UI will generate a list of options separated by the character '¬'. The valid list members are "M", "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", and "P". These values may be appended with the " with CBO" option.
- The "Sec / Logging" column is used on all but simple tags and is used to define the Write Logging used by Crimson for this tag. The valid options are "Default for Object", "Do Not Log Changes", "Log Changes by Users", "Log Changes by Users and Programs".

## Build and test commands

- Use the .NET 10 SDK.
- Build the reusable library: `dotnet build .\WorkTools.Core\WorkTools.Core.csproj -c Debug`
- Build the console runner: `dotnet build .\WorkTools\WorkTools.csproj -c Debug`
- Run the full test suite: `dotnet test .\WorkTools.Core.Tests\WorkTools.Core.Tests.csproj -c Debug`
- Run a single test: `dotnet test .\WorkTools.Core.Tests\WorkTools.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~TemplateExpanderTests.ParseTemplate_NumericSection_ParsesCorrectly"`
- Publish the WinUI app: `powershell -ExecutionPolicy Bypass -File .\publish.ps1 -Platform x64 -Configuration Release`
- CI uses the same MSBuild-based publish flow as `publish.ps1` for `x64` and `arm64` in `.github\workflows\release.yml`.
- There is no dedicated lint command in this repo; analyzer warnings surface during `dotnet build` and `dotnet test`.

## High-level architecture

- `WorkTools.Core\TemplateExpander.cs` contains the parsing, validation, stats, preview, and file-writing logic. Both front ends should delegate template expansion behavior to this library instead of re-implementing it.
- `WorkTools\Program.cs` is a thin console entry point that reads `Template.txt` and `TagsList.txt`, then writes `Output.txt`. Treat it as a simple file-based wrapper around `TemplateExpander.Generate`.
- `WorkTools.App` is the WinUI 3 desktop front end. `MainWindow.xaml` defines a two-pane editor/preview UI, and `MainWindow.xaml.cs` owns debounce timing, validation messages, preview tab creation, clipboard/save actions, and find/replace.
- The WinUI preview path is section-oriented: the window parses sections and tag names, expands each section in parallel through `TemplateExpander.ExpandSection`, then shows one tab per section while computing stats separately.
- Release packaging is Windows-specific. The checked-in script `publish.ps1` locates Visual Studio MSBuild and publishes a self-contained Windows App SDK build; plain `dotnet build .\WorkTools.slnx` can fail for `WorkTools.App` in CLI-only environments because the packaging tasks come from Visual Studio MSBuild.

## Key conventions

- Replacement tokens use the exact numbered literal pattern `{{1}}`, `{{2}}`, `{{3}}`, and so on.
- Replacement input is one row per line. Each row is tab-delimited, and column 1 maps to `{{1}}`, column 2 maps to `{{2}}`, etc. Blank lines are ignored.
- Output shape is test-backed and should not be changed casually: full generated output starts with a leading blank line, then each section writes the original bracketed section header, a blank line, the column header, and the expanded rows.
- UI tab labels intentionally differ from file output: `ExpandSection` strips the outer brackets for section tab headers, but `WriteSections` keeps the original bracketed section header in saved/generated output.
- Keep large-output behavior in mind when changing preview code. The WinUI app truncates each visible section preview to 500 lines for responsiveness, but copy/save actions still operate on the full generated content.
- Tests cover both inline sample templates and checked-in files in `WorkTools.Core.Tests` (`Test.txt`, `TextTemplate.txt`, `TestList.txt`). The test assembly enables method-level parallelization via `MSTestSettings.cs`, so new tests should avoid shared mutable state.

## Repository and Assistant Instructions

- Update repository and assistant instructions when requested and treat them as the source of truth for future code changes.
