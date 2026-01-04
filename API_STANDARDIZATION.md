# TagLibSharp2 API Standardization Plan

> Generated 2026-01-04

This document tracks API consistency work across all 14 supported formats.

## Completed Fixes (2026-01-04)

### 1. AsfFileReadResult.Value → .File ✅
All other formats use `.File` for the parsed file object. ASF was the only exception.

**Files changed:**
- `src/TagLibSharp2/Asf/AsfFile.cs`
- `src/TagLibSharp2/Core/MediaFile.cs`
- All ASF test files

### 2. SaveToFileAsync Convenience Overloads ✅
Added missing overloads to FlacFile and Mp3File:
- `SaveToFileAsync(path, fs, ct)` - re-reads from SourcePath
- `SaveToFileAsync(fs, ct)` - saves back to SourcePath

### 3. SourcePath Handling ✅
Fixed WavFile and AiffFile to set `SourcePath` in `ReadFromFile` and `ReadFromFileAsync`.

---

## API Surface Audit

### Result Type Naming Convention

| Format | Result Type | Property | Status |
|--------|-------------|----------|--------|
| FLAC | `FlacFileReadResult` | `.File` | ✅ |
| MP3 | `Mp3FileReadResult` | `.File` | ✅ |
| MP4 | `Mp4FileReadResult` | `.File` | ✅ |
| WAV | `WavFileReadResult` | `.File` | ✅ |
| AIFF | `AiffFileReadResult` | `.File` | ✅ |
| OggVorbis | `OggVorbisFileReadResult` | `.File` | ✅ |
| OggOpus | `OggOpusFileReadResult` | `.File` | ✅ |
| OggFlac | `OggFlacFileReadResult` | `.File` | ✅ |
| ASF | `AsfFileReadResult` | `.File` | ✅ Fixed |
| DSF | `DsfFileReadResult` | `.File` | ✅ |
| DFF | `DffFileReadResult` | `.File` | ✅ |
| WavPack | `WavPackFileReadResult` | `.File` | ✅ |
| MonkeysAudio | `MonkeysAudioFileReadResult` | `.File` | ✅ |
| Musepack | `MusepackFileReadResult` | `.File` | ✅ |

### SourcePath Handling

All file types should set `SourcePath` when read from disk.

| Format | ReadFromFile | ReadFromFileAsync | Status |
|--------|-------------|-------------------|--------|
| FLAC | ✅ | ✅ | Complete |
| MP3 | ✅ | ✅ | Complete |
| MP4 | ✅ | ✅ | Complete |
| WAV | ✅ | ✅ | Fixed 2026-01-04 |
| AIFF | ✅ | ✅ | Fixed 2026-01-04 |
| OggVorbis | ✅ | ✅ | Complete |
| OggOpus | ✅ | ✅ | Complete |
| OggFlac | ✅ | ✅ | Complete |
| ASF | ✅ | ✅ | Complete |
| DSF | ❓ | ❓ | Needs audit |
| DFF | ❓ | ❓ | Needs audit |
| WavPack | ❓ | ❓ | Needs audit |
| MonkeysAudio | ❓ | ❓ | Needs audit |
| Musepack | ❓ | ❓ | Needs audit |

### SaveToFileAsync Overloads

Convenience overloads that allow saving without re-passing original data.

| Format | `(path,data,fs,ct)` | `(path,fs,ct)` | `(fs,ct)` | Status |
|--------|---------------------|----------------|-----------|--------|
| FLAC | ✅ | ✅ | ✅ | Complete |
| MP3 | ✅ | ✅ | ✅ | Complete |
| MP4 | ❓ | ❓ | ❓ | Needs audit |
| WAV | ❓ | ❓ | ❓ | Needs audit |
| AIFF | ❓ | ❓ | ❓ | Needs audit |
| OggVorbis | ✅ | ❓ | ❓ | Partial |
| OggOpus | ❓ | ❓ | ❓ | Needs audit |
| OggFlac | ❓ | ❓ | ❓ | Needs audit |
| ASF | ❓ | ❓ | ❓ | Needs audit |
| DSF | ❓ | ❓ | ❓ | Needs audit |
| DFF | ❓ | ❓ | ❓ | Needs audit |
| WavPack | N/A | N/A | N/A | Read-only |
| MonkeysAudio | N/A | N/A | N/A | Read-only |
| Musepack | N/A | N/A | N/A | Read-only |

---

## Remaining Work

### High Priority

1. **Audit SourcePath for remaining formats**
   - DSF, DFF, WavPack, MonkeysAudio, Musepack
   - Verify ReadFromFile and ReadFromFileAsync set SourcePath

2. **Audit SaveToFileAsync overloads**
   - MP4, WAV, AIFF, OggVorbis, OggOpus, OggFlac, ASF, DSF, DFF
   - Add convenience overloads where missing

### Medium Priority

3. **Standardize error messages**
   - Audit all failure messages for consistency
   - Use format: "Invalid {format} file: {specific reason}"

4. **IFileSystem storage**
   - Formats that store the file system: FLAC, MP3
   - Add `_sourceFileSystem` field to remaining formats for SaveToFileAsync convenience

### Low Priority

5. **XML documentation audit**
   - Ensure all public APIs have consistent documentation
   - Add `<example>` sections for common operations

6. **Result type equality**
   - Ensure all result types implement IEquatable<T>
   - Ensure all result types have GetHashCode() implementations

---

## Design Decisions

### SourcePath Behavior
- `SourcePath` is `null` when parsed from `byte[]` or `ReadOnlySpan<byte>`
- `SourcePath` is set when read via `ReadFromFile` or `ReadFromFileAsync`
- `SourcePath` tracks the original file for later save operations

### SaveToFileAsync Overload Strategy
1. `(path, originalData, fs, ct)` - Explicit: user provides all data
2. `(path, fs, ct)` - Re-reads from SourcePath, writes to new path
3. `(fs, ct)` - Re-reads from SourcePath, overwrites original

Overloads 2 and 3 require `SourcePath` to be set (file must have been read from disk).

### Read-Only Formats
WavPack, MonkeysAudio, and Musepack are currently read-only and do not have write support.
Write support may be added in a future version.

---

## Version History

| Date | Changes |
|------|---------|
| 2026-01-04 | Fixed AsfFileReadResult.Value → .File |
| 2026-01-04 | Added SaveToFileAsync overloads to FlacFile/Mp3File |
| 2026-01-04 | Fixed SourcePath in WavFile/AiffFile ReadFromFile/Async |
