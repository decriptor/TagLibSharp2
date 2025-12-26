# TagLibSharp2 Architecture

TagLibSharp2 is a clean-room MIT-licensed library for reading and writing metadata in media files. This document describes the core architecture and design decisions.

## Design Principles

1. **Performance First** - `Span<T>` for zero-allocation views, minimal heap allocations, aggressive inlining
2. **Immutable by Default** - Core types are immutable; builders provide mutable construction
3. **Modern .NET** - Targets netstandard2.0+ with conditional compilation for optimal APIs
4. **Specification-Driven** - Implementations follow official format specifications (ID3, Xiph, etc.)

## Project Structure

```
tagsharp/
├── src/
│   └── TagLibSharp2/
│       ├── Core/                  # Foundation types
│       │   ├── BinaryData.cs      # Immutable binary wrapper
│       │   ├── BinaryDataBuilder.cs # Mutable builder
│       │   ├── Tag.cs             # Abstract tag base class
│       │   ├── Picture.cs         # Abstract picture base class
│       │   ├── TagReadResult.cs   # Result type for parsing
│       │   └── Polyfills.cs       # Framework compatibility
│       ├── Id3/                   # ID3 format support
│       │   ├── Id3v1Tag.cs        # ID3v1/v1.1 implementation
│       │   ├── Id3v1Genre.cs      # Genre lookup table
│       │   └── Id3v2/             # ID3v2 implementation
│       │       ├── Id3v2Tag.cs    # ID3v2 container
│       │       ├── Id3v2Header.cs # Header parsing
│       │       └── Frames/        # Frame implementations
│       ├── Xiph/                  # Xiph format support
│       │   ├── FlacFile.cs        # FLAC container
│       │   ├── VorbisComment.cs   # Vorbis comments
│       │   └── FlacPicture.cs     # FLAC picture blocks
│       └── Ogg/                   # Ogg container support
│           ├── OggVorbisFile.cs   # Ogg Vorbis files
│           ├── OggPage.cs         # Ogg page parsing
│           └── OggCrc.cs          # CRC computation
├── tests/
│   └── TagLibSharp2.Tests/            # Unit tests mirror source structure
└── docs/                          # Developer documentation
```

## Core Layer

The Core layer provides binary data handling primitives used by all format implementations.

### BinaryData (Immutable)

`BinaryData` is an immutable wrapper around `byte[]` providing:

- **Slicing**: `Slice()` returns new `BinaryData` (allocates). For zero-copy, use `.Span` directly
- **Search**: `IndexOf`/`LastIndexOf` for pattern matching
- **Conversions**: Endian-aware integer reads (`ToUInt16BE`, `ToInt32LE`, etc.)
- **Encoding**: String conversion with Latin-1, UTF-8, UTF-16 support
- **CRC**: Built-in CRC-32 and CRC-8 computation

```
┌─────────────────────────────────────────┐
│              BinaryData                  │
├─────────────────────────────────────────┤
│ - _data: byte[]                         │
├─────────────────────────────────────────┤
│ + Span: ReadOnlySpan<byte>    (zero-copy)│
│ + Memory: ReadOnlyMemory<byte>          │
│ + IndexOf(pattern): int                 │
│ + ToUInt32BE(offset): uint              │
│ + ToString(encoding): string            │
│ + ComputeCrc32(): uint                  │
└─────────────────────────────────────────┘
```

### BinaryDataBuilder (Mutable)

`BinaryDataBuilder` is the mutable counterpart for constructing binary data:

- **Fluent API**: All mutation methods return `this` for chaining
- **Growth Strategy**: Doubles capacity (min 256 bytes), grows to exact size if larger, clamps to max array length
- **Overflow Protection**: `CheckedAdd()` prevents integer overflow
- **Format-Specific**: `AddSyncSafeUInt32`, `AddUInt24BE/LE` for tag formats
- **Aggressive Inlining**: All `Add*` methods are marked `[AggressiveInlining]`

```
┌─────────────────────────────────────────┐
│          BinaryDataBuilder               │
├─────────────────────────────────────────┤
│ - _buffer: byte[]                       │
│ - _length: int                          │
├─────────────────────────────────────────┤
│ + Add(byte): this                       │
│ + AddUInt32BE(value): this              │
│ + AddSyncSafeUInt32(value): this        │
│ + Insert(index, data): this             │
│ + RemoveRange(index, count): this       │
│ + ToBinaryData(): BinaryData            │
└─────────────────────────────────────────┘
```

### Builder Pattern

The Builder pattern separates construction from use:

```csharp
// Construction (mutable)
var header = new BinaryDataBuilder()
    .Add(0x49, 0x44, 0x33)      // "ID3"
    .Add(0x04, 0x00)            // Version 2.4.0
    .Add(0x00)                  // Flags
    .AddSyncSafeUInt32(size)    // Size
    .ToBinaryData();            // Immutable result (allocates copy)

// Use (immutable)
var version = header[3];
var tagSize = header.ToSyncSafeUInt32(6);
```

## Allocation Behavior

Understanding when allocations occur is critical for performance:

| Operation | Allocates? | Notes |
|-----------|-----------|-------|
| `new BinaryData(byte[])` | No | Wraps existing array |
| `new BinaryData(ReadOnlySpan<byte>)` | **Yes** | Copies to new array |
| `data.Span` | No | Zero-copy view |
| `data.Slice(...)` | **Yes** | Creates new BinaryData with copied data |
| `data.ToString(encoding)` | **Yes** | Always allocates string |
| `builder.Add(...)` | Maybe | May trigger buffer growth |
| `builder.ToBinaryData()` | **Yes** | Copies current contents |

### Zero-Allocation Parsing Pattern

```csharp
// Read from file - one allocation
var data = new BinaryData(fileBytes);

// Parse using Span (zero allocations)
var span = data.Span;
var magic = span[..3];
var version = span[3];
var size = data.ToSyncSafeUInt32(6);

// Only allocate when storing results
if (needsSubset)
    var subset = data.Slice(offset, length); // Allocates only here
```

## Target Frameworks

| Target | Purpose |
|--------|---------|
| netstandard2.0 | Maximum compatibility (.NET Framework 4.6.1+, Mono, Unity) |
| netstandard2.1 | Span overloads, better performance |
| net8.0 | Modern .NET LTS |
| net10.0 | Latest .NET |

Conditional compilation (`#if NETSTANDARD2_0`) provides optimal implementations per target.

### Framework-Specific Considerations

- **netstandard2.0**: String encoding from `Span<byte>` requires intermediate array allocation
- **netstandard2.1+**: Direct `Encoding.GetString(ReadOnlySpan<byte>)` available
- **net8.0+**: Additional `BinaryPrimitives` optimizations

## Format Implementations

### Tag Layer

The abstract `Tag` base class provides common metadata properties:

```
┌─────────────────────────────────────────┐
│                 Tag                      │
├─────────────────────────────────────────┤
│ + Title: string?                        │
│ + Artist: string?                       │
│ + Album: string?                        │
│ + Year: uint?                           │
│ + Track: uint?                          │
│ + Genre: string?                        │
│ + Pictures: IReadOnlyList<Picture>      │
└─────────────────────────────────────────┘
           ▲
           │
    ┌──────┼──────────┐
    │      │          │
Id3v1Tag Id3v2Tag VorbisComment
```

### ID3 Implementation

```
Id3v1Tag (128 bytes, end of file)
├── Title (30 bytes)
├── Artist (30 bytes)
├── Album (30 bytes)
├── Year (4 bytes)
├── Comment (28-30 bytes)
├── Track (1 byte, v1.1 only)
└── Genre (1 byte index)

Id3v2Tag (variable size, start of file)
├── Id3v2Header
│   ├── Version (2.3 or 2.4)
│   ├── Flags
│   └── Size (syncsafe)
└── Frames[]
    ├── TextFrame (TIT2, TPE1, TALB, etc.)
    └── PictureFrame (APIC)
```

### Xiph Implementation

```
FlacFile
├── Magic: "fLaC" (4 bytes)
└── Metadata Blocks[]
    ├── STREAMINFO (required, first)
    ├── VORBIS_COMMENT (optional)
    ├── PICTURE (optional, multiple)
    └── PADDING (optional)

VorbisComment
├── Vendor string (UTF-8)
└── Fields[] (KEY=value, UTF-8)
    ├── TITLE, ARTIST, ALBUM
    ├── DATE, GENRE, TRACKNUMBER
    └── METADATA_BLOCK_PICTURE (base64)

OggVorbisFile
├── OggPage[] (with CRC validation)
└── Packets[]
    ├── Identification header
    ├── Comment header (VorbisComment)
    └── Setup header
```

## Future Architecture

### File I/O Layer (Planned)

File write operations and streaming support:

```
┌─────────────────────────────────────────┐
│            IFileAbstraction             │
├─────────────────────────────────────────┤
│ + Name: string                          │
│ + ReadStream: Stream                    │
│ + WriteStream: Stream                   │
│ + CloseStream(stream): void             │
└─────────────────────────────────────────┘
           ▲
           │
┌──────────┴──────────┐
│                     │
LocalFile         MemoryFile
```

## Performance Guidelines

1. **Use `.Span` for zero-allocation parsing** - Work with `ReadOnlySpan<byte>` directly; only create `BinaryData` when storing results
2. **Pre-size builders when length is known** - `new BinaryDataBuilder(expectedSize)` avoids growth allocations
3. **Inline hot paths** - `BinaryDataBuilder` applies `[AggressiveInlining]` to all `Add*` methods
4. **Avoid LINQ in loops** - Use explicit iteration for performance-critical code
5. **Prefer stackalloc for small buffers** - Up to ~256 bytes on stack
6. **Target netstandard2.1+ when possible** - Avoids string encoding allocations

## Error Handling

- **ArgumentException family** - Invalid parameters (null, out of range)
- **InvalidOperationException** - Invalid state (capacity exceeded)
- **FormatException** - Malformed file data (future)
- No silent failures - errors are thrown, not swallowed

## Thread Safety

- `BinaryData` - Thread-safe (immutable, no mutable state)
- `BinaryDataBuilder` - Not thread-safe (single-threaded construction expected)
- Future file types - Read operations thread-safe, writes require synchronization
