# TagLibSharp2 Implementation Roadmap

## Current Status Overview

Based on the specification in `/Users/sshaw/code/roon-8/Docs/TagLibSharp2/` and the current codebase.

### Legend
- ✅ Implemented
- 🔶 Partial (needs more work)
- ❌ Not started

---

## Phase 1: Core Infrastructure

| Component | Status | Notes |
|-----------|--------|-------|
| BinaryData (ByteVector equivalent) | ✅ | Immutable binary data with Span support |
| BinaryDataBuilder | ✅ | For building binary data |
| Tag base class | ✅ | Full property set including MusicBrainz, ReplayGain |
| IFileSystem abstraction | ✅ | With async support |
| Picture/IPicture | ✅ | PictureType enum complete |
| AudioProperties/IMediaProperties | ✅ | Base interfaces defined |
| TagValidation | ✅ | Validation result types |
| AtomicFileWriter | ✅ | Safe file writing |
| Endian readers | 🔶 | In BinaryData, may need standalone EndianReader class |
| Syncsafe integer utilities | 🔶 | In Id3v2Tag, could be extracted |
| Extended float (80-bit) for AIFF | ✅ | ExtendedFloat class with ToDouble/FromDouble |
| ITagLibStream interface | ❌ | Stream abstraction from spec (alternative to IFileSystem) |
| Format detection factory | ❌ | Magic byte detection per spec |

---

## Phase 2: Tag Formats

### ID3v1 Tag
| Feature | Status |
|---------|--------|
| Read | ✅ |
| Write | ✅ |
| Genre lookup | ✅ |
| ID3v1.1 track number | ✅ |

### ID3v2 Tag
| Feature | Status |
|---------|--------|
| Header parsing | ✅ |
| v2.3 support | ✅ |
| v2.4 support | ✅ |
| Extended header parsing | ✅ |
| Text frames (TIT2, TPE1, etc.) | ✅ |
| Picture frame (APIC) | ✅ |
| Comment frame (COMM) | ✅ |
| UserText frame (TXXX) | ✅ |
| URL frames (WXXX, WCOM, etc.) | ✅ |
| Lyrics frame (USLT) | ✅ |
| Sync lyrics frame (SYLT) | ✅ |
| Unique file ID (UFID) | ✅ |
| Involved people (TIPL/TMCL) | ✅ |
| Popularimeter (POPM) | ✅ |
| Private frame (PRIV) | ✅ |
| General object (GEOB) | ✅ |
| Chapter frame (CHAP) | ✅ |
| Table of contents (CTOC) | ✅ |
| Unsync decoding | 🔶 | May need verification |
| Footer support | ❌ |
| Compression support | ❌ |
| Encryption support | ❌ |

### Vorbis Comments (Xiph)
| Feature | Status |
|---------|--------|
| Read | ✅ |
| Write | ✅ |
| Multi-value fields | ✅ |
| METADATA_BLOCK_PICTURE (base64) | 🔶 | Needs verification |

### APE Tag
| Feature | Status |
|---------|--------|
| Read | ❌ |
| Write | ❌ |
| Binary items | ❌ |
| Cover art | ❌ |

### MP4/iTunes Atoms
| Feature | Status |
|---------|--------|
| Read | ❌ |
| Write | ❌ |
| Standard atoms (©nam, ©ART, etc.) | ❌ |
| trkn/disk parsing | ❌ |
| covr (cover art) | ❌ |
| Freeform atoms (----) | ❌ |
| Extended size atoms | ❌ |

### ASF/WMA Tags
| Feature | Status |
|---------|--------|
| Read | ❌ |
| Write | ❌ |
| Content Description Object | ❌ |
| Extended Content Description | ❌ |
| WM/Picture | ❌ |

### RIFF INFO Tags
| Feature | Status |
|---------|--------|
| Read | ✅ |
| Write | ✅ |
| Standard fields (INAM, IART, etc.) | ✅ |

---

## Phase 3: File Format Handlers (P0 - MVP)

### MP3/MPEG
| Feature | Status |
|---------|--------|
| Basic read | ✅ |
| ID3v2 at start | ✅ |
| ID3v1 at end | ✅ |
| MPEG frame header parsing | 🔶 |
| Xing/VBRI VBR header | ❌ |
| LAME tag info | ❌ |
| Duration calculation (VBR) | ❌ |
| Duration calculation (CBR) | 🔶 |
| APE tag support | ❌ |
| Write/save | ❌ |

### FLAC
| Feature | Status |
|---------|--------|
| Magic + metadata blocks | ✅ |
| STREAMINFO | ✅ |
| VORBIS_COMMENT | ✅ |
| PICTURE block | ✅ |
| CUESHEET | ✅ |
| SEEKTABLE | 🔶 |
| Write/save | ❌ |
| Metadata block reordering | ❌ |

### OGG Vorbis
| Feature | Status |
|---------|--------|
| Page parsing | ✅ |
| CRC validation | ✅ |
| Identification header | ✅ |
| Comment header | ✅ |
| Read | ✅ |
| Write | ✅ |
| Duration from granule | 🔶 |

### WAV
| Feature | Status |
|---------|--------|
| RIFF container | ✅ |
| fmt chunk (audio properties) | ✅ |
| data chunk | ✅ |
| LIST INFO tags | ✅ |
| ID3v2 chunk | ✅ |
| Write | ✅ |

### AIFF
| Feature | Status |
|---------|--------|
| FORM container | ❌ |
| COMM chunk | ❌ |
| Extended float parsing | ❌ |
| ID3 chunk | ❌ |
| Write | ❌ |

### AAC/ALAC (M4A/MP4)
| Feature | Status |
|---------|--------|
| Atom navigation | ❌ |
| moov/udta/meta/ilst path | ❌ |
| Audio properties from stsd | ❌ |
| Duration from mvhd/mdhd | ❌ |
| Tag read | ❌ |
| Tag write | ❌ |

### DSF (DSD)
| Feature | Status |
|---------|--------|
| DSD chunk | ❌ |
| fmt chunk | ❌ |
| ID3v2 at metadata offset | ❌ |
| Audio properties | ❌ |
| Write | ❌ |

### Opus
| Feature | Status |
|---------|--------|
| OpusHead parsing | ❌ |
| OpusTags parsing | ❌ |
| Pre-skip handling | ❌ |
| Duration (48kHz output) | ❌ |

---

## Phase 4: File Format Handlers (P1 - Extended)

| Format | Status | Notes |
|--------|--------|-------|
| DFF (DSDIFF) | ❌ | Read-only (no tag support) |
| WMA/ASF | ❌ | Full ASF container |
| WavPack | ❌ | APE tags |
| Musepack | ❌ | APE tags, SV7/SV8 |
| OGG FLAC | ❌ | FLAC in OGG container |
| Speex | ❌ | Vorbis Comments |
| TrueAudio | ❌ | ID3v2/ID3v1 |

---

## Phase 5: File Format Handlers (P2 - Niche)

| Format | Status | Notes |
|--------|--------|-------|
| Monkey's Audio (.ape) | ❌ | APE tags |
| MOD | ❌ | Title only, embedded |
| S3M | ❌ | Title only |
| IT | ❌ | Title + message |
| XM | ❌ | Title only |

---

## Implementation Priority Order

### MVP (Must Have for Roon Replacement)

1. **WAV** - Studio/archival format, very common
   - RIFF parsing
   - fmt chunk for audio properties
   - LIST INFO for basic tags
   - ID3v2 chunk for full tags

2. **AIFF** - Mac studio format
   - FORM container
   - COMM chunk with 80-bit float
   - ID3 chunk

3. **MP4/M4A** - Apple ecosystem (AAC/ALAC)
   - Atom navigation
   - iTunes metadata atoms
   - Cover art

4. **DSF** - DSD primary format
   - DSD/fmt chunk parsing
   - ID3v2 at offset

5. **Opus** - Modern streaming format
   - OpusHead/OpusTags
   - 48kHz output rate

6. **APE Tag Format** - Needed for Musepack/WavPack/Monkey's Audio
   - Required before those format handlers

7. **VBR Header Support** - Accurate MP3 duration
   - Xing/Info header
   - VBRI header

### Nice to Have (P1)

8. **WMA/ASF** - Legacy Windows format
9. **DFF** - DSD secondary format
10. **WavPack** - Lossless with lossy hybrid
11. **Musepack** - Audiophile lossy

### Low Priority (P2)

12. **OGG FLAC**
13. **Speex**
14. **TrueAudio**
15. **Monkey's Audio**
16. **MOD/S3M/IT/XM** - Tracker formats

---

## Critical Issues to Address

From spec document "Critical Implementation Notes":

1. **Integer Overflow in DSD Duration** - Use double arithmetic
2. **Encoding Class Name Collision** - Use fully qualified names
3. **LocalFileStream.Insert Off-by-One** - Fix loop condition
4. **GetTextFrame Return Type** - Proper nullable annotations
5. **XiphComment Empty String Handling** - Distinguish null vs empty
6. **OGG Page Parsing Infinite Loop** - Add safety limits
7. **Missing IDisposable Pattern** - Full dispose implementation
8. **Unsafe BitConverter Usage** - Use explicit endian readers
9. **ID3v1 Genre Property** - Static genre list access

---

## Testing Requirements

- [ ] Round-trip tests for all formats (read → modify → write → read)
- [ ] Cross-tagger compatibility (foobar2000, Mp3tag, iTunes, Picard)
- [ ] Large file support (>4GB)
- [ ] Corrupted file handling
- [ ] Performance benchmarks (<10ms tag reading)
- [ ] Memory efficiency tests (no full-file buffering)

---

## API Compatibility

Consider adding compatibility shim for TagLib# consumers:
- `TagLib.File.Create()` factory method
- `TagLib.Tag` unified tag interface
- Property mappings for common use cases

---

## Estimated Effort by Phase

| Phase | Files | Complexity | Notes |
|-------|-------|------------|-------|
| WAV | ✅ | ✅ | Complete with RIFF container + INFO + ID3v2 |
| AIFF | 3-4 | Medium | 80-bit float is tricky |
| MP4/M4A | 6-8 | High | Complex atom tree navigation |
| DSF | 3-4 | Low | Simple chunk format |
| Opus | 2-3 | Low | Similar to Vorbis |
| APE Tags | 2-3 | Medium | Needed for multiple formats |
| VBR Headers | 2 | Medium | Xing/VBRI parsing |
| ASF/WMA | 5-6 | High | GUID-based, complex |

---

*Generated: 2025-12-28*
