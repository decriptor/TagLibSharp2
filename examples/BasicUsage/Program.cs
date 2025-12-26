// TagLibSharp2 Basic Usage Examples
// Demonstrates core BinaryData and BinaryDataBuilder functionality

using System.Text;
using TagLibSharp2.Core;

Console.WriteLine ("=== TagLibSharp2 Basic Usage Examples ===\n");

// ============================================================
// 1. Creating BinaryData
// ============================================================
Console.WriteLine ("1. Creating BinaryData");
Console.WriteLine (new string ('-', 40));

// From byte array
byte[] bytes = [0x49, 0x44, 0x33]; // "ID3"
var data1 = new BinaryData (bytes);
Console.WriteLine ($"From byte array: {data1.ToHexString ()}");

// From span (zero-copy friendly)
ReadOnlySpan<byte> span = [0x01, 0x02, 0x03, 0x04];
var data2 = new BinaryData (span);
Console.WriteLine ($"From span: {data2.ToHexString ()}");

// Filled with zeros
var zeros = new BinaryData (8);
Console.WriteLine ($"8 zero bytes: {zeros.ToHexString ()}");

// Filled with value
var filled = new BinaryData (4, 0xFF);
Console.WriteLine ($"4 bytes of 0xFF: {filled.ToHexString ()}");

Console.WriteLine ();

// ============================================================
// 2. Reading Integers (Big-Endian and Little-Endian)
// ============================================================
Console.WriteLine ("2. Reading Integers");
Console.WriteLine (new string ('-', 40));

var intData = new BinaryData ([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

Console.WriteLine ($"Data: {intData.ToHexString ()}");
Console.WriteLine ($"UInt16 BE at 0: 0x{intData.ToUInt16BE ():X4} ({intData.ToUInt16BE ()})");
Console.WriteLine ($"UInt16 LE at 0: 0x{intData.ToUInt16LE ():X4} ({intData.ToUInt16LE ()})");
Console.WriteLine ($"UInt32 BE at 0: 0x{intData.ToUInt32BE ():X8}");
Console.WriteLine ($"UInt32 LE at 0: 0x{intData.ToUInt32LE ():X8}");
Console.WriteLine ($"UInt32 BE at 4: 0x{intData.ToUInt32BE (4):X8}");

Console.WriteLine ();

// ============================================================
// 3. ID3v2 Syncsafe Integers
// ============================================================
Console.WriteLine ("3. ID3v2 Syncsafe Integers");
Console.WriteLine (new string ('-', 40));

// Syncsafe integers use 7 bits per byte (MSB always 0)
// Used in ID3v2 tags to avoid false sync patterns
var syncsafe = new BinaryData ([0x00, 0x00, 0x02, 0x01]); // = 257 in syncsafe
Console.WriteLine ($"Syncsafe bytes: {syncsafe.ToHexString ()}");
Console.WriteLine ($"Decoded value: {syncsafe.ToSyncSafeUInt32 ()}");

// Create syncsafe from value
var encoded = BinaryData.FromSyncSafeUInt32 (268435455); // Max syncsafe value
Console.WriteLine ($"Max syncsafe (268435455) encoded: {encoded.ToHexString ()}");

Console.WriteLine ();

// ============================================================
// 4. String Encoding/Decoding
// ============================================================
Console.WriteLine ("4. String Encoding/Decoding");
Console.WriteLine (new string ('-', 40));

// Latin-1 (ISO-8859-1) - common in ID3v1/ID3v2.3
var latin1 = BinaryData.FromStringLatin1 ("Hello World");
Console.WriteLine ($"Latin-1 'Hello World': {latin1.ToHexString ()}");
Console.WriteLine ($"Decoded: {latin1.ToStringLatin1 ()}");

// UTF-8 - used in ID3v2.4 and Vorbis comments
var utf8 = BinaryData.FromStringUtf8 ("Hello ");
Console.WriteLine ($"UTF-8 with emoji: {utf8.ToHexString ()}");
Console.WriteLine ($"Decoded: {utf8.ToStringUtf8 ()}");

// UTF-16 with BOM - used in ID3v2
var utf16 = BinaryData.FromStringUtf16 ("Test", includeBom: true);
Console.WriteLine ($"UTF-16 LE with BOM: {utf16.ToHexString ()}");

Console.WriteLine ();

// ============================================================
// 5. Using BinaryDataBuilder
// ============================================================
Console.WriteLine ("5. Using BinaryDataBuilder (Fluent API)");
Console.WriteLine (new string ('-', 40));

// Build an ID3v2 header structure
using var builder = new BinaryDataBuilder ();

var header = builder
    .Add ((ReadOnlySpan<byte>)"ID3"u8)  // ID3 identifier
    .Add (0x04)                          // Version major (2.4)
    .Add (0x00)                          // Version minor
    .Add (0x00)                          // Flags
    .AddSyncSafeUInt32 (1024)            // Tag size (syncsafe)
    .ToBinaryData ();

Console.WriteLine ($"ID3v2.4 Header: {header.ToHexString ()}");
Console.WriteLine ($"  Identifier: {header.Slice (0, 3).ToStringLatin1 ()}");
Console.WriteLine ($"  Version: 2.{header[3]}.{header[4]}");
Console.WriteLine ($"  Tag Size: {header.Slice (6, 4).ToSyncSafeUInt32 ()} bytes");

Console.WriteLine ();

// ============================================================
// 6. Pattern Matching and Searching
// ============================================================
Console.WriteLine ("6. Pattern Matching and Searching");
Console.WriteLine (new string ('-', 40));

var fileData = new BinaryData ([
    0x49, 0x44, 0x33, 0x04, 0x00, // ID3v2.4 header start
    0x00, 0x00, 0x00, 0x10, 0x00, // ... more header
    0xFF, 0xFB, 0x90, 0x00        // MP3 frame sync
]);

Console.WriteLine ($"Data: {fileData.ToHexString ()}");
Console.WriteLine ($"Starts with 'ID3': {fileData.StartsWith ("ID3"u8)}");
Console.WriteLine ($"Contains 0xFF 0xFB (MP3 sync): {fileData.Contains ([0xFF, 0xFB])}");
Console.WriteLine ($"Index of MP3 sync: {fileData.IndexOf ([0xFF, 0xFB])}");

Console.WriteLine ();

// ============================================================
// 7. Slicing and Manipulation
// ============================================================
Console.WriteLine ("7. Slicing and Manipulation");
Console.WriteLine (new string ('-', 40));

var original = new BinaryData ([0x01, 0x02, 0x03, 0x04, 0x05]);
Console.WriteLine ($"Original: {original.ToHexString ()}");

var sliced = original.Slice (1, 3);
Console.WriteLine ($"Slice(1, 3): {sliced.ToHexString ()}");

var padded = original.PadRight (8);
Console.WriteLine ($"PadRight(8): {padded.ToHexString ()}");

var rangeSlice = original[1..^1]; // Using range syntax
Console.WriteLine ($"[1..^1]: {rangeSlice.ToHexString ()}");

Console.WriteLine ();

// ============================================================
// 8. CRC Computation
// ============================================================
Console.WriteLine ("8. CRC Computation");
Console.WriteLine (new string ('-', 40));

var crcData = new BinaryData ("123456789"u8);
Console.WriteLine ($"Data: '123456789'");
Console.WriteLine ($"CRC-8: 0x{crcData.ComputeCrc8 ():X2}");
Console.WriteLine ($"CRC-16 CCITT: 0x{crcData.ComputeCrc16Ccitt ():X4}");
Console.WriteLine ($"CRC-32: 0x{crcData.ComputeCrc32 ():X8}");

Console.WriteLine ();
Console.WriteLine ("=== Examples Complete ===");
