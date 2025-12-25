# Core Types Reference

This document covers the foundational types in `TagSharp.Core`.

## BinaryData

Immutable wrapper for binary data with parsing utilities.

### Creation

```csharp
// From byte array (wraps, no copy)
var data = new BinaryData(new byte[] { 0x49, 0x44, 0x33 });

// From span (copies data)
Span<byte> span = stackalloc byte[] { 0x49, 0x44, 0x33 };
var data = new BinaryData(span);

// With fill value
var zeros = new BinaryData(10);           // 10 zero bytes
var filled = new BinaryData(10, 0xFF);    // 10 bytes of 0xFF

// Empty instance
var empty = BinaryData.Empty;
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Length` | `int` | Number of bytes |
| `Count` | `int` | Alias for Length (IReadOnlyCollection) |
| `IsEmpty` | `bool` | True if length is zero |
| `Span` | `ReadOnlySpan<byte>` | Zero-copy span view |
| `Memory` | `ReadOnlyMemory<byte>` | Memory view |
| `this[int]` | `byte` | Indexer for byte access |
| `this[Range]` | `BinaryData` | Range indexer (allocates) |

### Slicing

> **Note**: `Slice()` allocates a new `BinaryData`. For zero-copy views, use `.Span` directly.

```csharp
var data = new BinaryData(new byte[] { 0, 1, 2, 3, 4, 5 });

// Slice from offset (allocates new BinaryData)
var tail = data.Slice(2);           // [2, 3, 4, 5]

// Slice with length (allocates)
var mid = data.Slice(1, 3);         // [1, 2, 3]

// Via Span (zero-copy, preferred for parsing)
ReadOnlySpan<byte> view = data.Span.Slice(2, 2);  // [2, 3]

// Range syntax (allocates)
var range = data[1..4];             // [1, 2, 3]
```

### Search

```csharp
var data = new BinaryData(new byte[] { 0, 1, 2, 1, 2, 3 });

// Find first occurrence
int pos = data.IndexOf(new byte[] { 1, 2 });       // 1

// Find with start offset
int pos = data.IndexOf(new byte[] { 1, 2 }, 2);    // 3

// Find last occurrence
int pos = data.LastIndexOf(new byte[] { 1, 2 });   // 3

// Contains check
bool found = data.Contains(new byte[] { 2, 3 });   // true
bool hasByte = data.Contains((byte)0x02);          // true

// Prefix/suffix checks
bool starts = data.StartsWith(new byte[] { 0, 1 }); // true
bool ends = data.EndsWith(new byte[] { 2, 3 });     // true
```

### Integer Conversions

All methods support an optional `offset` parameter (default 0).

| Method | Returns | Description |
|--------|---------|-------------|
| `ToUInt16BE(offset)` | `ushort` | Big-endian 16-bit unsigned |
| `ToUInt16LE(offset)` | `ushort` | Little-endian 16-bit unsigned |
| `ToUInt32BE(offset)` | `uint` | Big-endian 32-bit unsigned |
| `ToUInt32LE(offset)` | `uint` | Little-endian 32-bit unsigned |
| `ToUInt64BE(offset)` | `ulong` | Big-endian 64-bit unsigned |
| `ToUInt64LE(offset)` | `ulong` | Little-endian 64-bit unsigned |
| `ToInt16BE(offset)` | `short` | Big-endian 16-bit signed |
| `ToInt16LE(offset)` | `short` | Little-endian 16-bit signed |
| `ToInt32BE(offset)` | `int` | Big-endian 32-bit signed |
| `ToInt32LE(offset)` | `int` | Little-endian 32-bit signed |
| `ToInt64BE(offset)` | `long` | Big-endian 64-bit signed |
| `ToInt64LE(offset)` | `long` | Little-endian 64-bit signed |
| `ToUInt24BE(offset)` | `uint` | Big-endian 24-bit unsigned |
| `ToUInt24LE(offset)` | `uint` | Little-endian 24-bit unsigned |
| `ToSyncSafeUInt32(offset)` | `uint` | ID3v2 syncsafe integer |

```csharp
var data = new BinaryData(new byte[] { 0x00, 0x00, 0x01, 0x00 });

uint valueBE = data.ToUInt32BE();   // 256 (0x00000100)
uint valueLE = data.ToUInt32LE();   // 65536 (0x00010000)
```

### String Conversions

```csharp
// Convenience methods
string latin1 = data.ToStringLatin1();
string utf8 = data.ToStringUtf8();
string utf16 = data.ToStringUtf16();

// With encoding
string text = data.ToString(Encoding.UTF8);

// Null-terminated (stops at first null)
string terminated = data.ToStringLatin1NullTerminated();
string terminated = data.ToStringUtf8NullTerminated();
string terminated = data.ToStringUtf16NullTerminated();

// For offset/length, use Slice first
string partial = data.Slice(4, 10).ToStringUtf8();
```

### Manipulation (Returns New BinaryData)

```csharp
// Padding
var padded = data.PadRight(10, 0x00);    // Pad to 10 bytes on right
var padded = data.PadLeft(10, 0x00);     // Pad to 10 bytes on left

// Trimming
var trimmed = data.TrimEnd(0x00);        // Remove trailing zeros
var trimmed = data.TrimStart(0x00);      // Remove leading zeros
var trimmed = data.Trim(0x00);           // Remove both ends

// Resize
var resized = data.Resize(20, 0x00);     // Resize to 20 bytes, pad with zeros

// Concatenation
var combined = data1.Add(data2);
var combined = BinaryData.Concat(data1, data2, data3);
var combined = data1 + data2;            // Operator overload
```

### Hex Conversion

```csharp
// To hex string
string hex = data.ToHexString();         // "0102030405"
string hex = data.ToHexStringUpper();    // "0102030405"

// From hex string
var data = BinaryData.FromHexString("0102030405");
```

### CRC Computation

```csharp
uint crc32 = data.ComputeCrc32();
byte crc8 = data.ComputeCrc8();
ushort crc16 = data.ComputeCrc16Ccitt();
```

### Static Factories

| Method | Bytes | Description |
|--------|-------|-------------|
| `FromUInt16BE(value)` | 2 | Big-endian unsigned 16-bit |
| `FromUInt16LE(value)` | 2 | Little-endian unsigned 16-bit |
| `FromUInt32BE(value)` | 4 | Big-endian unsigned 32-bit |
| `FromUInt32LE(value)` | 4 | Little-endian unsigned 32-bit |
| `FromUInt64BE(value)` | 8 | Big-endian unsigned 64-bit |
| `FromUInt64LE(value)` | 8 | Little-endian unsigned 64-bit |
| `FromInt16BE(value)` | 2 | Big-endian signed 16-bit |
| `FromInt16LE(value)` | 2 | Little-endian signed 16-bit |
| `FromInt32BE(value)` | 4 | Big-endian signed 32-bit |
| `FromInt32LE(value)` | 4 | Little-endian signed 32-bit |
| `FromInt64BE(value)` | 8 | Big-endian signed 64-bit |
| `FromInt64LE(value)` | 8 | Little-endian signed 64-bit |
| `FromUInt24BE(value)` | 3 | Big-endian 24-bit |
| `FromSyncSafeUInt32(value)` | 4 | ID3v2 syncsafe |
| `FromStringLatin1(value)` | varies | Latin-1 encoded |
| `FromStringUtf8(value)` | varies | UTF-8 encoded |
| `FromStringUtf16(value, bom)` | varies | UTF-16 with optional BOM |
| `FromHexString(hex)` | varies | From hex string |

### Exceptions

| Exception | Condition |
|-----------|-----------|
| `ArgumentOutOfRangeException` | Negative length in constructor |
| `ArgumentOutOfRangeException` | Index/offset out of bounds |
| `ArgumentOutOfRangeException` | Syncsafe value > 0x0FFFFFFF |
| `ArgumentOutOfRangeException` | 24-bit value > 0xFFFFFF |
| `FormatException` | Invalid hex string |

---

## BinaryDataBuilder

Mutable builder for constructing `BinaryData` instances.

### Creation

```csharp
// Default capacity (256 bytes)
var builder = new BinaryDataBuilder();

// Custom initial capacity (recommended when size is known)
var builder = new BinaryDataBuilder(1024);
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Length` | `int` | Current data length |
| `Capacity` | `int` | Buffer capacity |
| `Span` | `ReadOnlySpan<byte>` | View of current data |
| `Memory` | `ReadOnlyMemory<byte>` | Memory view |
| `this[int]` | `byte` | Get/set byte at index |

### Adding Data

All `Add*` methods return `this` for fluent chaining.

```csharp
var builder = new BinaryDataBuilder()
    .Add(0x49)                          // Single byte
    .Add(0x44, 0x33)                    // Multiple bytes (params)
    .Add(existingData.Span)             // From span
    .Add(otherBinaryData);              // From BinaryData
```

### Adding Integers

| Method | Bytes | Description |
|--------|-------|-------------|
| `AddUInt16BE(value)` | 2 | Big-endian unsigned 16-bit |
| `AddUInt16LE(value)` | 2 | Little-endian unsigned 16-bit |
| `AddUInt32BE(value)` | 4 | Big-endian unsigned 32-bit |
| `AddUInt32LE(value)` | 4 | Little-endian unsigned 32-bit |
| `AddUInt64BE(value)` | 8 | Big-endian unsigned 64-bit |
| `AddUInt64LE(value)` | 8 | Little-endian unsigned 64-bit |
| `AddInt16BE(value)` | 2 | Big-endian signed 16-bit |
| `AddInt16LE(value)` | 2 | Little-endian signed 16-bit |
| `AddInt32BE(value)` | 4 | Big-endian signed 32-bit |
| `AddInt32LE(value)` | 4 | Little-endian signed 32-bit |
| `AddInt64BE(value)` | 8 | Big-endian signed 64-bit |
| `AddInt64LE(value)` | 8 | Little-endian signed 64-bit |
| `AddUInt24BE(value)` | 3 | Big-endian 24-bit (max 0xFFFFFF) |
| `AddUInt24LE(value)` | 3 | Little-endian 24-bit (max 0xFFFFFF) |
| `AddSyncSafeUInt32(value)` | 4 | ID3v2 syncsafe (max 0x0FFFFFFF) |

### Adding Strings

```csharp
builder
    .AddStringLatin1("ISO-8859-1 text")
    .AddStringUtf8("UTF-8 text")
    .AddStringUtf16("UTF-16 text", includeBom: true);

// Null-terminated variants
builder
    .AddStringLatin1NullTerminated("text")   // + 0x00
    .AddStringUtf8NullTerminated("text")     // + 0x00
    .AddStringUtf16NullTerminated("text");   // + 0x00 0x00
```

### Fill Operations

```csharp
builder
    .AddZeros(10)           // 10 zero bytes
    .AddFill(0xFF, 16);     // 16 bytes of 0xFF
```

### Insert and Remove

```csharp
// Insert at position (shifts existing data)
builder.Insert(0, new BinaryData(header));

// Remove range
builder.RemoveRange(index: 4, count: 2);
```

### Buffer Management

```csharp
// Pre-allocate capacity
builder.EnsureCapacity(1024);

// Clear data, keep buffer
builder.Clear();

// Clear data, release buffer (resets to zero capacity)
builder.Reset();
```

### Output

```csharp
// Get immutable BinaryData (copies current contents)
BinaryData result = builder.ToBinaryData();

// Get byte array copy
byte[] array = builder.ToArray();
```

### Exceptions

| Exception | Condition |
|-----------|-----------|
| `ArgumentOutOfRangeException` | Negative capacity in constructor |
| `ArgumentOutOfRangeException` | Index out of bounds |
| `ArgumentOutOfRangeException` | Syncsafe value > 0x0FFFFFFF |
| `ArgumentOutOfRangeException` | 24-bit value > 0xFFFFFF |
| `ArgumentNullException` | Null encoding in AddString |
| `InvalidOperationException` | Size would exceed max array length |

---

## Performance Characteristics

### Allocation Behavior

| Operation | Allocates? | Notes |
|-----------|-----------|-------|
| `new BinaryData(byte[])` | No | Wraps existing array |
| `new BinaryData(ReadOnlySpan<byte>)` | **Yes** | Copies to new array |
| `data.Span` | No | Zero-copy view |
| `data.Slice(...)` | **Yes** | Creates new BinaryData with copied data |
| `data.ToString(...)` | **Yes** | Always allocates string |
| `builder.Add(...)` | Maybe | May trigger buffer growth |
| `builder.ToBinaryData()` | **Yes** | Copies current contents |

### Zero-Allocation Parsing Pattern

```csharp
// Read from file - one allocation
var data = new BinaryData(fileBytes);

// Parse using Span (zero allocations)
var span = data.Span;
bool isId3 = span[..3].SequenceEqual("ID3"u8);
byte majorVersion = span[3];
byte minorVersion = span[4];
byte flags = span[5];
uint size = data.ToSyncSafeUInt32(6);

// Only allocate when storing final results
var tagData = data.Slice(10, (int)size);
```

---

## Real-World Examples

### ID3v2 Header

```csharp
public BinaryData CreateId3v2Header(uint tagSize)
{
    return new BinaryDataBuilder()
        .Add(0x49, 0x44, 0x33)      // "ID3" magic
        .Add(0x04, 0x00)            // Version 2.4.0
        .Add(0x00)                  // Flags
        .AddSyncSafeUInt32(tagSize) // Tag size (syncsafe)
        .ToBinaryData();
}
```

### Vorbis Comment

```csharp
public BinaryData CreateVorbisComment(string vendor, string[] comments)
{
    var vendorBytes = Encoding.UTF8.GetByteCount(vendor);
    var builder = new BinaryDataBuilder()
        .AddUInt32LE((uint)vendorBytes)  // Vendor string length
        .AddStringUtf8(vendor)
        .AddUInt32LE((uint)comments.Length);  // Number of comments

    foreach (var comment in comments)
    {
        var commentBytes = Encoding.UTF8.GetByteCount(comment);
        builder
            .AddUInt32LE((uint)commentBytes)
            .AddStringUtf8(comment);
    }

    return builder.ToBinaryData();
}
```

### FLAC StreamInfo

```csharp
public BinaryData CreateStreamInfoBlock(
    ushort minBlockSize,
    ushort maxBlockSize,
    uint minFrameSize,
    uint maxFrameSize)
{
    return new BinaryDataBuilder()
        .AddUInt16BE(minBlockSize)
        .AddUInt16BE(maxBlockSize)
        .AddUInt24BE(minFrameSize)
        .AddUInt24BE(maxFrameSize)
        // ... additional fields
        .ToBinaryData();
}
```

### Parsing Binary Data (Zero-Allocation)

```csharp
public (int Major, int Minor, uint Size) ParseId3v2Header(BinaryData header)
{
    // Validate magic using Span (zero-allocation)
    if (!header.Span[..3].SequenceEqual("ID3"u8))
        throw new FormatException("Not an ID3v2 header");

    byte majorVersion = header[3];
    byte minorVersion = header[4];
    byte flags = header[5];
    uint size = header.ToSyncSafeUInt32(6);

    return (majorVersion, minorVersion, size);
}
```
