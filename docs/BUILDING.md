# Building TagSharp

## Prerequisites

- **.NET SDK 8.0** or later (includes support for all target frameworks)
- Optional: .NET 10 SDK for net10.0 target

Verify your installation:

```bash
dotnet --list-sdks
```

## Quick Start

```bash
git clone https://github.com/decriptor/tagsharp.git
cd tagsharp
dotnet build
dotnet test
```

## Build Commands

| Command | Description |
|---------|-------------|
| `dotnet build` | Build all projects (Release by default) |
| `dotnet build -c Debug` | Build in Debug configuration |
| `dotnet test` | Run all tests on all target frameworks |
| `dotnet pack` | Create NuGet package |
| `dotnet clean` | Clean build outputs |

## Target Frameworks

### Library (TagSharp)

| Target | Purpose |
|--------|---------|
| `netstandard2.0` | .NET Framework 4.6.1+, Mono, Unity, Xamarin |
| `netstandard2.1` | .NET Core 3.0+, better Span support |
| `net8.0` | Modern .NET LTS |
| `net10.0` | Latest .NET |

### Tests (TagSharp.Tests)

| Target | Purpose |
|--------|---------|
| `net8.0` | Primary test target (LTS) |
| `net10.0` | Latest .NET |

## Running Tests

### Basic Commands

```bash
# Run all tests on all frameworks
dotnet test

# Run on specific framework
dotnet test -f net8.0

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Filtering Tests

```bash
# Run tests matching class name
dotnet test --filter "ClassName~BinaryData"

# Run tests matching method name pattern
dotnet test --filter "Name~AddUInt32"

# Run single specific test
dotnet test --filter "FullyQualifiedName~BinaryDataBuilderTests.AddUInt32BE_WritesCorrectBytes"

# Run all tests for a feature
dotnet test --filter "ClassName~BinaryDataBuilder"
```

### Multi-Framework Testing

Tests run on all target frameworks by default. Always verify tests pass on **all** frameworks before submitting PRs:

```bash
# Run on all frameworks (default)
dotnet test

# Verify specific framework
dotnet test -f net8.0
dotnet test -f net10.0
```

**Why this matters**: Framework-specific bugs can occur due to:
- Different `BinaryPrimitives` implementations
- Span API availability (netstandard2.0 vs 2.1+)
- Encoding behavior differences

## Debugging Tests

### IDE Integration

**VS Code**: Click "Run Test" or "Debug Test" CodeLens above test methods

**Visual Studio**: Right-click test in Test Explorer → Debug

**Rider**: Click green play button → Debug

### CLI Debugging

```bash
# Run single test with detailed output
dotnet test --filter "Name~TestName" --logger "console;verbosity=detailed"

# See assertion failure details
dotnet test --filter "Name~TestName" -- MSTest.Logging.Verbosity=4
```

### Common Debug Patterns

```csharp
// Inspect actual values in failing test
Assert.AreEqual(expected, actual, $"Actual bytes: {BitConverter.ToString(actual)}");

// Use Fail to inspect state
Assert.Fail($"Builder state: Length={builder.Length}, Capacity={builder.Capacity}");
```

## Code Analysis

The project uses strict code analysis:

```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisLevel>latest-all</AnalysisLevel>
```

All warnings are treated as errors. Fix any analyzer warnings before submitting PRs.

### Test Project Analysis

Test projects use relaxed analyzer rules to allow:
- **CA1707**: Underscores in test names (`AddUInt32BE_WritesCorrectBytes`)
- **CA1515**: Public type visibility requirements

Main library remains strict with `latest-all`.

### Format Check

```bash
# Check formatting
dotnet format --verify-no-changes

# Auto-fix formatting
dotnet format
```

## Project Structure

```
tagsharp/
├── Directory.Build.props      # Shared build settings
├── Directory.Packages.props   # Central package management
├── TagSharp.slnx              # Solution file
├── src/
│   └── TagSharp/
│       ├── TagSharp.csproj    # Library project
│       └── Core/              # Source files
└── tests/
    └── TagSharp.Tests/
        ├── TagSharp.Tests.csproj
        └── Core/              # Test files
```

## Build Configuration

### Directory.Build.props

Shared settings for all projects:

- `LangVersion`: latest
- `Nullable`: enable
- `ImplicitUsings`: enable
- Deterministic builds enabled

### Conditional Compilation

The codebase uses conditional compilation for framework-specific optimizations:

```csharp
#if NETSTANDARD2_0
    // Fallback for older frameworks
    encoding.GetBytes(value, 0, value.Length, _buffer, _length);
#else
    // Modern API
    encoding.GetBytes(value, _buffer.AsSpan(_length));
#endif
```

Symbols available:
- `NETSTANDARD2_0` - netstandard2.0 target
- `NETSTANDARD2_1` - netstandard2.1 target
- `NET8_0_OR_GREATER` - net8.0+

## Creating Packages

```bash
# Create NuGet package
dotnet pack -c Release

# Package location
ls src/TagSharp/bin/Release/*.nupkg
```

## Continuous Integration

The project is configured for CI with:

- Deterministic builds for reproducibility
- All warnings as errors
- Full analyzer coverage
- Multi-framework test runs

## Troubleshooting

### "SDK not found"

Install the required .NET SDK from https://dotnet.microsoft.com/download

### "Framework not found"

For net10.0 issues, either:
1. Install .NET 10 SDK
2. Or temporarily remove net10.0 from TargetFrameworks in test project

### Analyzer Errors

Run `dotnet format` to fix most style issues. For analyzer-specific errors, check the error code (e.g., CA1062) and apply the recommended fix.

### Test Discovery Issues

If tests aren't discovered:
```bash
# Rebuild and run
dotnet build
dotnet test --no-build
```
