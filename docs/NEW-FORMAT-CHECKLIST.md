# New Format Implementation Checklist

This checklist covers everything needed to implement a new audio format in TagLibSharp2. Follow TDD (Test-Driven Development) - write tests first, then implement.

The process around it (behaviour survey, twin table, library rules, the test kit and pre-review) is in [IMPLEMENTATION-GUIDE.md](IMPLEMENTATION-GUIDE.md).

## Pre-Implementation Research

- [ ] **Obtain official specification** - Use primary sources (ISO standards, format creator docs, RFC)
- [ ] **Document specification source** in code comments with URLs
- [ ] **Identify magic bytes** for format detection (typically 4-16 bytes)
- [ ] **Map metadata fields** to Tag base class properties
- [ ] **Identify audio properties** available (duration, bitrate, sample rate, channels, bits per sample)
- [ ] **Check for existing implementations** in other tools (foobar2000, Picard) for field name conventions
- [ ] **Note any format variants** (versions, optional features, edge cases)

## Clean Room Requirements

**CRITICAL**: TagLibSharp2 is MIT-licensed. Do NOT reference GPL/LGPL code.

- [ ] Implementation based solely on format specifications
- [ ] No copying from TagLib# or other GPL projects
- [ ] OK to use similar public API design (APIs aren't copyrightable)
- [ ] OK to use test files from other projects as test inputs (they're just data)
- [ ] Document specification references in XML doc comments

## Directory Structure

Create files following existing patterns:

```
src/TagLibSharp2/{Format}/
├── {Format}File.cs              # Main file class (extends MediaFile)
├── {Format}FileReadResult.cs    # Result type (if complex)
├── {Format}Tag.cs               # Tag implementation (if format-specific)
├── {Format}AudioProperties.cs   # Audio properties (if format-specific)
└── {Format}Constants.cs         # Magic bytes, GUIDs, etc. (if needed)

tests/TagLibSharp2.Tests/{Format}/
├── {Format}FileTests.cs         # Core parsing/rendering tests
├── {Format}TagTests.cs          # Tag-specific tests
├── {Format}RoundTripTests.cs    # Read-modify-write tests
├── {Format}TestBuilder.cs       # Programmatic file creation helper
└── {Format}EdgeCaseTests.cs     # Edge cases, large files, etc.
```

## Core Implementation Checklist

### File Class (`{Format}File.cs`)

- [ ] Inherit from `MediaFile` base class
- [ ] Implement `IDisposable` pattern (call `base.Dispose()`)
- [ ] Static `Parse(ReadOnlySpan<byte> data)` method returning result type
- [ ] Static `ReadFromFile(string path, IFileSystem? fileSystem = null)` method
- [ ] Static `ReadFromFileAsync(string path, IFileSystem? fileSystem = null, CancellationToken ct = default)` method
- [ ] `Render()` method returning `BinaryData` (for write support)
- [ ] `SaveToFile(string path, IFileSystem? fileSystem = null)` method
- [ ] `SaveToFileAsync(string path, IFileSystem? fileSystem = null, CancellationToken ct = default)` method
- [ ] Override `Tag` property (create lazily if needed)
- [ ] Override `AudioProperties` property
- [ ] Convenience properties delegating to Tag (Title, Artist, Album, etc.)

### Result Types

Use the established result type pattern:

```csharp
public readonly struct {Format}FileReadResult : IEquatable<{Format}FileReadResult>
{
    public {Format}File? File { get; }
    public string? Error { get; }
    public bool IsSuccess => File is not null && Error is null;

    // Private constructor, static factory methods
    public static {Format}FileReadResult Success({Format}File file) => ...
    public static {Format}FileReadResult Failure(string error) => ...

    // IEquatable implementation
    public bool Equals({Format}FileReadResult other) => ...
    public override bool Equals(object? obj) => ...
    public override int GetHashCode() => ...
}
```

### Tag Implementation

If format uses existing tag type (ID3v2, VorbisComment, APE):
- [ ] Reuse existing tag class
- [ ] Document which tag type is used

If format needs custom tag:
- [ ] Inherit from `Tag` base class
- [ ] Override `TagType` property returning appropriate `TagTypes` enum value
- [ ] Implement all relevant properties from base class
- [ ] Implement `Clear()` method
- [ ] Implement `Render()` method returning `BinaryData`
- [ ] Add `Parse(ReadOnlySpan<byte> data)` static method

### Audio Properties

- [ ] Create `{Format}AudioProperties` class or use base `AudioProperties`
- [ ] Implement: `Duration`, `Bitrate`, `SampleRate`, `Channels`, `BitsPerSample`
- [ ] Use `TimeSpan` for duration
- [ ] Document units (bitrate in kbps, sample rate in Hz)

### MediaFile Integration

Update `src/TagLibSharp2/Core/MediaFile.cs`:

- [ ] Add magic bytes constant: `private static readonly byte[] {Format}Magic = ...`
- [ ] Add detection in `DetectFormat()` method
- [ ] Add case in `OpenFromData()` switch
- [ ] Add `Open{Format}()` helper method (optional)
- [ ] Update `SupportedExtensions` if applicable

## Code Style Requirements

### Naming Conventions

- [ ] File-scoped namespaces
- [ ] PascalCase for public members
- [ ] _camelCase for private fields
- [ ] No regions (`#region` forbidden per project rules)
- [ ] XML doc comments on all public members

### Patterns to Follow

- [ ] Use `ReadOnlySpan<byte>` for parsing (avoid allocations)
- [ ] Use `BinaryData` and `BinaryDataBuilder` for rendering
- [ ] Use result types instead of exceptions for validation
- [ ] Immutable structs for parsed data where appropriate
- [ ] `is null` / `is not null` (not `== null`)
- [ ] Expression-bodied members for simple properties
- [ ] Primary constructors where appropriate

### Error Handling

- [ ] Return failure results, don't throw exceptions for invalid data
- [ ] Include descriptive error messages
- [ ] Validate all sizes before allocating memory
- [ ] Check for integer overflow on size calculations
- [ ] Handle truncated files gracefully

## Testing Requirements

### Unit Tests (`{Format}FileTests.cs`)

Format Detection:
- [ ] `Parse_ValidMagic_ReturnsSuccess()`
- [ ] `Parse_InvalidMagic_ReturnsFailure()`
- [ ] `Parse_EmptyInput_ReturnsFailure()`
- [ ] `Parse_TruncatedHeader_ReturnsFailure()`

Parsing:
- [ ] `Parse_ExtractsAudioProperties()`
- [ ] `Parse_ExtractsMetadata()`
- [ ] `Parse_HandlesAllTagFields()`

File I/O:
- [ ] `ReadFromFile_ValidPath_ReturnsSuccess()`
- [ ] `ReadFromFileAsync_ValidPath_ReturnsSuccess()`
- [ ] `ReadFromFileAsync_Cancellation_Throws()`

### Tag Tests (`{Format}TagTests.cs`)

- [ ] Test each metadata property (Title, Artist, Album, Year, Genre, Track, etc.)
- [ ] Test extended properties (AlbumArtist, Composer, Conductor, DiscNumber)
- [ ] Test MusicBrainz IDs
- [ ] Test ReplayGain fields
- [ ] Test Pictures/Cover art
- [ ] Test Clear() removes all metadata

### Round-Trip Tests (`{Format}RoundTripTests.cs`)

- [ ] `RoundTrip_Title_Preserves()`
- [ ] `RoundTrip_AllBasicFields_Preserve()`
- [ ] `RoundTrip_ExtendedFields_Preserve()`
- [ ] `RoundTrip_Pictures_Preserve()`
- [ ] `RoundTrip_UnknownData_Preserved()` (if applicable)
- [ ] `SaveToFile_WritesCorrectly()`
- [ ] `SaveToFileAsync_WritesCorrectly()`

### Edge Case Tests

- [ ] Large files (>4GB if format supports 64-bit sizes)
- [ ] Empty metadata
- [ ] Maximum field lengths
- [ ] Unicode in all text fields
- [ ] Multiple pictures
- [ ] Zero-length audio (metadata only)

### Malformed Input Tests (add to `MalformedInputTests.cs`)

- [ ] `{Format}File_EmptyInput_ReturnsFailure()`
- [ ] `{Format}File_TruncatedHeader_ReturnsFailure()`
- [ ] `{Format}File_InvalidMagic_ReturnsFailure()`
- [ ] `{Format}File_OversizedClaim_DoesNotAllocate()`
- [ ] `{Format}File_RandomData_DoesNotCrash()`
- [ ] `{Format}File_CorruptedMetadata_ReturnsFailure()`

### Cross-Tagger Compatibility Tests (add to `CrossTaggerCompatibilityTests.cs`)

- [ ] Verify field names match industry standards (Picard, foobar2000, Mp3tag)
- [ ] Test case-sensitivity/insensitivity per format spec
- [ ] Full metadata round-trip test

### Test Builder (`{Format}TestBuilder.cs`)

Create programmatic file builder for tests:

```csharp
public static class {Format}TestBuilder
{
    public static byte[] CreateMinimal{Format}(string? title = null, string? artist = null);
    public static byte[] CreateWithMetadata(/* params */);
    public static byte[] CreateWithAudioProperties(uint sampleRate, ushort channels, ...);
    // Helper methods for building format-specific structures
}
```

### Test Categories

Add appropriate test categories:

```csharp
[TestClass]
[TestCategory("Unit")]
[TestCategory("{Format}")]
public class {Format}FileTests { }
```

## Build and Quality Checks

- [ ] `dotnet build` passes with no errors
- [ ] `dotnet build` passes with no warnings (or warnings are documented)
- [ ] `dotnet test` - all tests pass on net8.0 and net10.0
- [ ] `dotnet format` - code is formatted (run before committing)
- [ ] No analyzer warnings (CA*, IDE*, etc.)

## Documentation Updates

### Code Documentation

- [ ] XML doc comments on all public types and members
- [ ] `<summary>` describing purpose
- [ ] `<remarks>` with format details, spec references
- [ ] `<param>` for all parameters
- [ ] `<returns>` for all return values
- [ ] `<example>` for complex APIs (optional)

### Project Documentation

- [ ] Update `README.md` supported formats list
- [ ] Update `docs/MIGRATION-FROM-TAGLIB.md` if API differs
- [ ] Add format-specific documentation if complex

## Commit Guidelines

### Commit Structure

Break work into logical commits:
1. Add test builder and initial failing tests
2. Implement core parsing (make tests pass)
3. Implement tag support
4. Implement audio properties extraction
5. Implement write support
6. Add MediaFile integration
7. Add edge case and malformed input tests

### Commit Message Format

```
Add {Format} format support

- Implement {Format}File with read/write support
- Parse audio properties (duration, bitrate, sample rate, channels)
- Support {TagType} tags for metadata
- Add comprehensive test coverage
- Integrate with MediaFile factory for auto-detection

Specification: {URL}
```

## Final Review Checklist

Before marking complete:

- [ ] All tests pass: `dotnet test`
- [ ] Code formatted: `dotnet format`
- [ ] No compiler warnings
- [ ] XML documentation complete
- [ ] Specification sources documented
- [ ] Edge cases handled
- [ ] Malformed input doesn't crash
- [ ] Large file support verified (if applicable)
- [ ] Cross-tagger compatibility verified
- [ ] README updated
- [ ] Clean git history with meaningful commits

## Example Formats to Reference

Good examples of implemented formats in this codebase:

| Format | Notes |
|--------|-------|
| `Xiph/FlacFile.cs` | VorbisComment tags, pictures, audio properties |
| `Ogg/OggVorbisFile.cs` | Container format with codec packets |
| `Ogg/OggOpusFile.cs` | Similar to Vorbis with different codec |
| `Mp4/Mp4File.cs` | Complex box-based format |
| `Dsf/DsfFile.cs` | DSD format with ID3v2 tags |
| `Dff/DffFile.cs` | Chunk-based format, 64-bit sizes |
| `Ape/MonkeysAudioFile.cs` | APE tags at end of file |
| `WavPack/WavPackFile.cs` | APE tags with lossless audio |
| `Riff/WavFile.cs` | RIFF container format |
| `Aiff/AiffFile.cs` | IFF container format |

## Common Pitfalls to Avoid

1. **Integer overflow** - Use `uint`/`ulong` for sizes, check before casting to `int`
2. **Allocation bombs** - Validate claimed sizes against actual data length
3. **Wrong endianness** - Most formats use little-endian, but check spec
4. **Syncsafe integers** - ID3v2 uses special encoding, don't forget
5. **String encoding** - UTF-8 vs UTF-16 vs Latin1, null terminators
6. **Off-by-one errors** - Size includes/excludes header? Check spec
7. **Padding requirements** - Some formats require word/dword alignment
8. **Tag placement** - Beginning vs end of file, or both
9. **Case sensitivity** - Field names may be case-sensitive or not
10. **Empty vs null** - Distinguish between missing field and empty string
