# Contributing to TagSharp

Thank you for your interest in contributing to TagSharp! This document provides guidelines for contributing to the project.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR_USERNAME/tagsharp.git`
3. Create a branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Run tests: `dotnet test`
6. Commit and push
7. Open a Pull Request

## Development Setup

### Prerequisites

- .NET 8.0 SDK or later
- Your preferred IDE (VS Code, Visual Studio, Rider)

### Build & Test

```bash
dotnet build           # Build all targets
dotnet test            # Run all tests
dotnet test --filter "ClassName~BinaryData"  # Run specific tests
```

See [docs/BUILDING.md](docs/BUILDING.md) for detailed build instructions.

## Code Style

We use `.editorconfig` to enforce consistent style. Key conventions:

### Formatting

- **Indentation**: Tabs (4-space width)
- **Space before parentheses**: `Method ()` not `Method()`
- **Braces**: New line for types/methods only
- **Line length**: 120 characters max

```csharp
// Correct
public void ProcessData (byte[] data)
{
    if (data is null)
        throw new ArgumentNullException (nameof (data));

    foreach (var item in data) {
        Process (item);
    }
}
```

### Naming

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `BinaryDataBuilder` |
| Methods | PascalCase | `AddUInt32BE` |
| Parameters | camelCase | `initialCapacity` |
| Private fields | _camelCase | `_buffer` |
| Constants | PascalCase | `DefaultCapacity` |

### Preferences

- **var**: Use when type is apparent
- **Null checks**: Use `is null` / `is not null` (not `== null`)
- **File-scoped namespaces**: Yes
- **Primary constructors**: Yes, when appropriate
- **No regions**: Never use `#region`

## Pull Request Guidelines

### Before Submitting

- [ ] Code compiles without warnings (`TreatWarningsAsErrors` is enabled)
- [ ] All tests pass on all target frameworks (`dotnet test`)
- [ ] New code has tests
- [ ] Code follows project style (run `dotnet format` to check)

### PR Title Format

Use a clear, descriptive title:

- `Add BinaryDataBuilder for mutable binary construction`
- `Fix overflow in RemoveRange validation`
- `Improve AddString performance for netstandard2.0`

### PR Description

Include:
- **What**: Brief description of changes
- **Why**: Motivation or issue being fixed
- **Testing**: How you tested the changes

## Writing Tests

We use MSTest. Tests should be:

- **Focused**: One behavior per test
- **Named clearly**: `MethodName_Scenario_ExpectedResult`
- **Independent**: No shared mutable state

```csharp
[TestMethod]
public void AddUInt32BE_WritesCorrectBytes ()
{
    var builder = new BinaryDataBuilder ();

    builder.AddUInt32BE (0x12345678);

    Assert.AreEqual (4, builder.Length);
    Assert.AreEqual ((byte)0x12, builder[0]);
    Assert.AreEqual ((byte)0x78, builder[3]);
}
```

### Test Coverage Expectations

All public APIs should have tests covering:

- **Happy path**: Expected inputs and outputs
- **Boundary conditions**: Zero, max values, empty input
- **Error conditions**: Invalid inputs, expected exceptions

```csharp
// Happy path
[TestMethod]
public void AddUInt24BE_WritesCorrectBytes () { ... }

// Boundary
[TestMethod]
public void AddUInt24BE_MaxValue_WritesCorrectBytes () { ... }
[TestMethod]
public void AddUInt24BE_ZeroValue_WritesCorrectBytes () { ... }

// Error condition
[TestMethod]
public void AddUInt24BE_ExceedsMax_ThrowsArgumentOutOfRange () { ... }
```

## Performance Considerations

TagSharp prioritizes performance. When contributing:

### When to Consider Performance

Benchmark when modifying:
- Methods with `[MethodImpl(AggressiveInlining)]`
- Loops processing data (>1KB)
- String encoding operations
- Buffer allocation strategies

### Performance Guidelines

1. **Avoid allocations in hot paths** - Use `Span<T>` and `stackalloc`
2. **Use `[AggressiveInlining]`** - For small, frequently-called methods (all `Add*` methods use this)
3. **Pre-size builders** - When final size is known
4. **Prefer `.Span` over `.Slice()`** - Span is zero-copy; Slice allocates

### Benchmarking (Future)

For significant performance changes, benchmarks will be added to `tests/TagSharp.Benchmarks/`:

```csharp
[MemoryDiagnoser]
public class BinaryDataBenchmarks
{
    [Benchmark]
    public void ParseHeader_1KB () { ... }
}
```

Run with: `dotnet run -c Release --project tests/TagSharp.Benchmarks`

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the overall design.

Key patterns:
- **Immutable + Builder**: `BinaryData` (immutable) + `BinaryDataBuilder` (mutable)
- **Specification-driven**: Follow official format specs (ID3, Xiph, etc.)
- **Multi-target**: Support netstandard2.0 through net10.0

## Reporting Issues

When reporting bugs:

1. Check existing issues first
2. Include .NET version and OS
3. Provide minimal reproduction steps
4. Include relevant error messages/stack traces

## Questions?

- Open a GitHub Discussion for questions
- Open an Issue for bugs or feature requests

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
