# WorkTools repository instructions

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

- The template format is section-based. A section starts with a bracketed header like `[Numeric.1.0]`, `[Flag.1.0]`, or `[String.1.0]`; the first non-empty line after that header is the tab-delimited column header; later non-empty lines are data rows until the next bracketed header.
- The replacement token is the exact literal `{{TagName}}`. Tag input is one tag per line, and blank tag lines are ignored.
- Output shape is test-backed and should not be changed casually: full generated output starts with a leading blank line, then each section writes the original bracketed section header, a blank line, the column header, and the expanded rows.
- UI tab labels intentionally differ from file output: `ExpandSection` strips the outer brackets for section tab headers, but `WriteSections` keeps the original bracketed section header in saved/generated output.
- Keep large-output behavior in mind when changing preview code. The WinUI app truncates each visible section preview to 500 lines for responsiveness, but copy/save actions still operate on the full generated content.
- Tests cover both inline sample templates and checked-in files in `WorkTools.Core.Tests` (`Test.txt`, `TextTemplate.txt`, `TestList.txt`). The test assembly enables method-level parallelization via `MSTestSettings.cs`, so new tests should avoid shared mutable state.
