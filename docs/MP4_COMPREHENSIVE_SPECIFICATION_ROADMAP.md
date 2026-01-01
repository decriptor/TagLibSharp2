# MP4/M4A Comprehensive Specification Roadmap

**Version:** 1.0
**Date:** 2025-12-31
**Target Implementation:** TagLibSharp2 v0.4.0+

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Complete Box Type Catalog](#1-complete-box-type-catalog)
3. [Data Type Reference](#2-data-type-reference)
4. [Audio Property Extraction Path](#3-audio-property-extraction-path)
5. [Metadata Atom Complete Reference](#4-metadata-atom-complete-reference)
6. [Edge Cases and Compatibility Matrix](#5-edge-cases-and-compatibility-matrix)
7. [Implementation Roadmap by Priority](#6-implementation-roadmap-by-priority)
8. [Authoritative Sources](#authoritative-sources)

---

## Executive Summary

This document provides a **complete** specification roadmap for implementing MP4/M4A support in TagLibSharp2, covering the full ISO Base Media File Format (ISO 14496-12), MP4 File Format (ISO 14496-14), Apple QuickTime extensions, and iTunes-specific metadata.

**Key Standards:**
- **ISO/IEC 14496-12** - ISO Base Media File Format (ISOBMFF)
- **ISO/IEC 14496-14** - MP4 File Format
- **Apple QuickTime File Format** - QuickTime/MOV extensions
- **iTunes Metadata** - iTunes-style tagging (no official spec, reverse-engineered)

**Official Registry:** [mp4ra.org](https://mp4ra.org/) - MP4 Registration Authority (maintained by Apple Inc.)

---

## 1. Complete Box Type Catalog

All box types defined in ISO 14496-12 and registered extensions. Each box is categorized by priority for implementation.

### 1.1 Core Container Boxes

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `ftyp` | File Type Box | Leaf | **Required** | **P0** | ISO 14496-12 §4.3 |
| `moov` | Movie Box | Container | **Required** | **P0** | ISO 14496-12 §8.2.1 |
| `mdat` | Media Data Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.2.2 |
| `free` | Free Space Box | Leaf | Optional | P2 | ISO 14496-12 §8.1.2 |
| `skip` | Skip Box | Leaf | Optional | P2 | ISO 14496-12 §8.1.2 |
| `uuid` | User Extension Box | Either | Optional | P3 | ISO 14496-12 §8.1.1 |
| `pdin` | Progressive Download Box | Leaf | Optional | P3 | ISO 14496-12 §8.43 |

### 1.2 Movie-Level Boxes (inside `moov`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `mvhd` | Movie Header Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.2.2 |
| `trak` | Track Box | Container | **Required** | **P0** | ISO 14496-12 §8.3.1 |
| `udta` | User Data Box | Container | Optional | **P0** | ISO 14496-12 §8.10.1 |
| `meta` | Metadata Box | Container | Optional | **P0** | ISO 14496-12 §8.11.1 |
| `mvex` | Movie Extends Box | Container | Optional | P3 | ISO 14496-12 §8.8.1 |
| `iods` | Initial Object Descriptor | Leaf | Optional | P3 | ISO 14496-14 §5.1 |

### 1.3 Track-Level Boxes (inside `trak`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `tkhd` | Track Header Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.3.2 |
| `mdia` | Media Box | Container | **Required** | **P0** | ISO 14496-12 §8.4.1 |
| `edts` | Edit Box | Container | Optional | P1 | ISO 14496-12 §8.6.1 |
| `tref` | Track Reference Box | Container | Optional | P2 | ISO 14496-12 §8.3.3 |
| `trgr` | Track Group Box | Container | Optional | P3 | ISO 14496-12 §8.3.4 |
| `udta` | User Data Box (track) | Container | Optional | P1 | ISO 14496-12 §8.10.1 |
| `meta` | Metadata Box (track) | Container | Optional | P1 | ISO 14496-12 §8.11.1 |

### 1.4 Media Boxes (inside `mdia`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `mdhd` | Media Header Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.4.2 |
| `hdlr` | Handler Reference Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.4.3 |
| `minf` | Media Information Box | Container | **Required** | **P0** | ISO 14496-12 §8.4.4 |
| `elng` | Extended Language Box | Leaf | Optional | P1 | ISO 14496-12 §8.4.6 |

### 1.5 Media Information Boxes (inside `minf`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `vmhd` | Video Media Header Box | Leaf | Video only | P3 | ISO 14496-12 §12.1.2 |
| `smhd` | Sound Media Header Box | Leaf | **Audio only** | **P0** | ISO 14496-12 §12.2.2 |
| `hmhd` | Hint Media Header Box | Leaf | Hint only | P3 | ISO 14496-12 §12.4.2 |
| `nmhd` | Null Media Header Box | Leaf | Other media | P3 | ISO 14496-12 §12.6.2 |
| `dinf` | Data Information Box | Container | **Required** | **P0** | ISO 14496-12 §8.7.1 |
| `stbl` | Sample Table Box | Container | **Required** | **P0** | ISO 14496-12 §8.5.1 |

### 1.6 Data Information Boxes (inside `dinf`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `dref` | Data Reference Box | Container | **Required** | **P0** | ISO 14496-12 §8.7.2 |
| `url ` | Data Entry URL Box | Leaf | Optional | P1 | ISO 14496-12 §8.7.2 |
| `urn ` | Data Entry URN Box | Leaf | Optional | P3 | ISO 14496-12 §8.7.2 |

### 1.7 Sample Table Boxes (inside `stbl`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `stsd` | Sample Description Box | Container | **Required** | **P0** | ISO 14496-12 §8.5.2 |
| `stts` | Time-to-Sample Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.6.1.2 |
| `ctts` | Composition Time to Sample | Leaf | Optional | P2 | ISO 14496-12 §8.6.1.3 |
| `cslg` | Composition to Decode Box | Leaf | Optional | P3 | ISO 14496-12 §8.6.1.4 |
| `stsc` | Sample-to-Chunk Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.7.4 |
| `stsz` | Sample Size Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.7.3.2 |
| `stz2` | Compact Sample Size Box | Leaf | Optional | P2 | ISO 14496-12 §8.7.3.3 |
| `stco` | Chunk Offset Box | Leaf | **Required** | P1 | ISO 14496-12 §8.7.5 |
| `co64` | 64-bit Chunk Offset Box | Leaf | Optional | P1 | ISO 14496-12 §8.7.5 |
| `stss` | Sync Sample Box | Leaf | Optional | P2 | ISO 14496-12 §8.6.2 |
| `stsh` | Shadow Sync Sample Box | Leaf | Optional | P3 | ISO 14496-12 §8.6.3 |
| `padb` | Padding Bits Box | Leaf | Optional | P3 | ISO 14496-12 §8.7.6 |
| `stdp` | Sample Degradation Priority | Leaf | Optional | P3 | ISO 14496-12 §8.7.7 |
| `sdtp` | Independent/Disposable Samples | Leaf | Optional | P3 | ISO 14496-12 §8.6.4 |
| `sbgp` | Sample to Group Box | Leaf | Optional | P3 | ISO 14496-12 §8.9.2 |
| `sgpd` | Sample Group Description Box | Leaf | Optional | P3 | ISO 14496-12 §8.9.3 |
| `subs` | Sub-Sample Information Box | Leaf | Optional | P3 | ISO 14496-12 §8.7.8 |
| `saiz` | Sample Auxiliary Info Sizes | Leaf | Optional | P3 | ISO 14496-12 §8.7.9 |
| `saio` | Sample Auxiliary Info Offsets | Leaf | Optional | P3 | ISO 14496-12 §8.7.10 |

### 1.8 Sample Description Entry Types (inside `stsd`)

| 4CC Code | Full Name | Media Type | Priority | Specification |
|----------|-----------|------------|----------|---------------|
| `mp4a` | MPEG-4 Audio Sample Entry | Audio/AAC | **P0** | ISO 14496-14 §5.6 |
| `alac` | Apple Lossless Audio Codec | Audio/ALAC | **P0** | Apple Extension |
| `ac-3` | Dolby Digital (AC-3) | Audio | P1 | ETSI TS 102 366 |
| `ec-3` | Dolby Digital Plus (E-AC-3) | Audio | P1 | ETSI TS 102 366 |
| `Opus` | Opus Audio | Audio | P2 | RFC 8486 |
| `mp3.` | MP3 Audio | Audio | P2 | ISO 14496-3 Amd.3 |
| `sawb` | AMR-WB Audio | Audio | P3 | 3GPP TS 26.244 |
| `samr` | AMR-NB Audio | Audio | P3 | 3GPP TS 26.244 |

### 1.9 Audio Sample Entry Sub-Boxes

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `esds` | Elementary Stream Descriptor | Leaf | AAC/MPEG-4 | **P0** | ISO 14496-14 §5.6.1 |
| `alac` | ALAC Magic Cookie | Leaf | ALAC only | **P0** | Apple ALAC spec |
| `dac3` | Dolby Digital (AC-3) Info | Leaf | AC-3 only | P1 | ETSI TS 102 366 |
| `dec3` | E-AC-3 Specific Box | Leaf | E-AC-3 only | P1 | ETSI TS 102 366 |
| `dOps` | Opus Specific Box | Leaf | Opus only | P2 | RFC 8486 |
| `wave` | Wave Atom (legacy) | Container | Legacy | P2 | QuickTime legacy |
| `chan` | Channel Layout | Leaf | Optional | P1 | Apple Extension |
| `btrt` | Bit Rate Box | Leaf | Optional | P2 | ISO 14496-12 §8.5.2.2 |

### 1.10 Edit List Boxes (inside `edts`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `elst` | Edit List Box | Leaf | **Required** | P1 | ISO 14496-12 §8.6.6 |

### 1.11 User Data Boxes (inside `udta`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `meta` | Metadata Box | Container | Optional | **P0** | ISO 14496-12 §8.11.1 |
| `cprt` | Copyright Box | Leaf | Optional | P1 | ISO 14496-12 §8.10.2 |
| `name` | Name Box | Leaf | Optional | P2 | QuickTime |
| `chpl` | Chapter List Box | Container | Optional | P1 | Nero/iTunes |

### 1.12 Metadata Boxes (inside `meta`)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `hdlr` | Handler Box | Leaf | **Required** | **P0** | ISO 14496-12 §8.4.3 |
| `ilst` | Metadata Item List Box | Container | iTunes | **P0** | iTunes metadata |
| `keys` | Metadata Keys Box | Container | Optional | P1 | Apple metadata |
| `dinf` | Data Information Box | Container | Optional | P3 | ISO 14496-12 §8.7.1 |
| `iloc` | Item Location Box | Leaf | ISO meta | P3 | ISO 14496-12 §8.11.3 |
| `ipro` | Item Protection Box | Container | ISO meta | P3 | ISO 14496-12 §8.11.5 |
| `iinf` | Item Information Box | Container | ISO meta | P3 | ISO 14496-12 §8.11.6 |
| `xml ` | XML Box | Leaf | ISO meta | P3 | ISO 14496-12 §8.11.2 |
| `bxml` | Binary XML Box | Leaf | ISO meta | P3 | ISO 14496-12 §8.11.2 |
| `pitm` | Primary Item Box | Leaf | ISO meta | P3 | ISO 14496-12 §8.11.4 |

### 1.13 Movie Fragment Boxes (for streaming/DASH)

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `moof` | Movie Fragment Box | Container | Optional | P3 | ISO 14496-12 §8.8.4 |
| `mfhd` | Movie Fragment Header Box | Leaf | Fragment | P3 | ISO 14496-12 §8.8.5 |
| `traf` | Track Fragment Box | Container | Fragment | P3 | ISO 14496-12 §8.8.6 |
| `tfhd` | Track Fragment Header Box | Leaf | Fragment | P3 | ISO 14496-12 §8.8.7 |
| `trun` | Track Fragment Run Box | Leaf | Fragment | P3 | ISO 14496-12 §8.8.8 |
| `tfdt` | Track Fragment Decode Time | Leaf | Fragment | P3 | ISO 14496-12 §8.8.12 |
| `mfra` | Movie Fragment Random Access | Container | Fragment | P3 | ISO 14496-12 §8.8.9 |
| `tfra` | Track Fragment Random Access | Leaf | Fragment | P3 | ISO 14496-12 §8.8.10 |
| `mfro` | Movie Fragment Random Access Offset | Leaf | Fragment | P3 | ISO 14496-12 §8.8.11 |
| `mehd` | Movie Extends Header Box | Leaf | Optional | P3 | ISO 14496-12 §8.8.2 |
| `trex` | Track Extends Box | Leaf | Fragment | P3 | ISO 14496-12 §8.8.3 |
| `sidx` | Segment Index Box | Leaf | Optional | P3 | ISO 14496-12 §8.16.3 |
| `ssix` | Sub-Segment Index Box | Leaf | Optional | P3 | ISO 14496-12 §8.16.4 |
| `styp` | Segment Type Box | Leaf | Optional | P3 | ISO 14496-12 §8.16.2 |

### 1.14 DRM/Protection Boxes

| 4CC Code | Full Name | Container/Leaf | Required | Priority | Specification |
|----------|-----------|----------------|----------|----------|---------------|
| `pssh` | Protection System Specific Header | Leaf | DRM | P3 | ISO 23001-7 (CENC) |
| `sinf` | Protection Scheme Information Box | Container | DRM | P2 | ISO 14496-12 §8.12.1 |
| `frma` | Original Format Box | Leaf | DRM | P2 | ISO 14496-12 §8.12.2 |
| `schm` | Scheme Type Box | Leaf | DRM | P2 | ISO 14496-12 §8.12.5 |
| `schi` | Scheme Information Box | Container | DRM | P2 | ISO 14496-12 §8.12.6 |
| `tenc` | Track Encryption Box | Leaf | CENC | P3 | ISO 23001-7 §8.2 |

**Note on DRM:** Priority P2 for detection only (to skip protected files), not for decryption.

### 1.15 Additional ISO/QuickTime Boxes

| 4CC Code | Full Name | Container/Leaf | Priority | Specification |
|----------|-----------|----------------|----------|---------------|
| `colr` | Color Information Box | Leaf | P2 | ISO 14496-12 §12.1.5 |
| `pasp` | Pixel Aspect Ratio Box | Leaf | P3 | ISO 14496-12 §12.1.4 |
| `fiel` | Field/Frame Information Box | Leaf | P3 | QuickTime |
| `clap` | Clean Aperture Box | Leaf | P3 | ISO 14496-12 §12.1.4 |
| `load` | Track Load Settings | Leaf | P3 | QuickTime legacy |
| `imap` | Track Input Map | Leaf | P3 | QuickTime legacy |
| `matt` | Track Matte | Container | P3 | QuickTime legacy |

---

## 2. Data Type Reference

### 2.1 Integer Types

| Type | Size | Endianness | Signed | Range |
|------|------|------------|--------|-------|
| `uint8` | 1 byte | N/A | Unsigned | 0 to 255 |
| `uint16` | 2 bytes | Big-endian | Unsigned | 0 to 65,535 |
| `uint32` | 4 bytes | Big-endian | Unsigned | 0 to 4,294,967,295 |
| `uint64` | 8 bytes | Big-endian | Unsigned | 0 to 2^64-1 |
| `int8` | 1 byte | N/A | Signed | -128 to 127 |
| `int16` | 2 bytes | Big-endian | Signed | -32,768 to 32,767 |
| `int32` | 4 bytes | Big-endian | Signed | -2,147,483,648 to 2,147,483,647 |

### 2.2 Fixed-Point Numbers (16.16 Format)

**Structure:** 32 bits total = 16-bit integer part + 16-bit fractional part

**Format:** Big-endian, signed

**Range:** -32,768.0 to +32,767.99998 (resolution: 1.5×10^-5)

**Conversion:**
```csharp
// Float to 16.16 fixed point
int32 ToFixed16_16(double value) => (int)Math.Round(value * 65536.0);

// 16.16 fixed point to float
double FromFixed16_16(int32 fixedValue) => fixedValue / 65536.0;
```

**Common Uses:**
- Sample rate in audio sample entries (e.g., 44100.0 Hz = 0x0000AC44 0x00000000)
- Volume in `mvhd` and `tkhd` (1.0 = 0x01000000)
- Playback rate in `mvhd` (1.0 = 0x00010000)
- Matrix transformation values in `tkhd`

### 2.3 Fixed-Point Numbers (8.8 Format)

**Structure:** 16 bits total = 8-bit integer part + 8-bit fractional part

**Format:** Big-endian

**Range:** 0 to 255.996 (resolution: 1/256)

**Common Uses:**
- Volume in `smhd` (Sound Media Header)

### 2.4 Fixed-Point Numbers (2.30 Format)

**Structure:** 32 bits total = 2-bit integer part + 30-bit fractional part

**Common Uses:**
- Matrix transformation values (some implementations)

### 2.5 String Types

| Type | Encoding | Null-Terminated | Max Length | Usage |
|------|----------|-----------------|------------|-------|
| Pascal String | UTF-8 or MacRoman | No | 255 bytes | QuickTime legacy |
| C String | UTF-8 | Yes | Variable | Handler names, URIs |
| Counted String | UTF-8 | No | By prefix | Some metadata |
| UTF-8 String | UTF-8 | Varies | Variable | Modern metadata |
| UTF-16 String | UTF-16 BE | Varies | Variable | Some ID3-style tags |

### 2.6 Date/Time Formats

#### ISO Base Media Format Time

**Type:** `uint32` or `uint64` (version-dependent)

**Epoch:** January 1, 1904, 00:00:00 UTC (Macintosh epoch)

**Units:** Seconds since epoch

**Conversion to Unix Timestamp:**
```csharp
// ISO BMFF time to Unix timestamp
const long MacToUnixEpochOffset = 2082844800L; // Seconds between 1904 and 1970
long unixTime = isoTime - MacToUnixEpochOffset;
```

**Common Uses:**
- `mvhd.creation_time`
- `mvhd.modification_time`
- `mdhd.creation_time`
- `mdhd.modification_time`
- `tkhd.creation_time`
- `tkhd.modification_time`

#### iTunes Metadata Date Strings

**Format:** "YYYY-MM-DD" or just "YYYY"

**Atom:** `©day` (day atom, despite the name)

**Examples:**
- "2025"
- "2025-12"
- "2025-12-31"

### 2.7 Language Codes

#### Traditional (mdhd)

**Type:** `uint16` (3×5-bit packed values)

**Standard:** ISO 639-2/T (3-letter codes)

**Encoding:** Each character is (ASCII value - 0x60), packed into 5 bits

**Example:**
```
"eng" = 'e'=101, 'n'=110, 'g'=103
Packed: (101-96)<<10 | (110-96)<<5 | (103-96) = 0x15C7
```

**Special Codes:**
- `und` (0x55C4) = undetermined language
- `mul` = multiple languages

#### Modern (elng)

**Type:** Null-terminated UTF-8 string

**Standard:** BCP-47 / RFC 5646 (IETF language tags)

**Examples:**
- "en"
- "en-US"
- "fr-FR"
- "zh-Hans" (Simplified Chinese)

### 2.8 Box Header Structures

#### Basic Box

```
aligned(8) class Box {
    unsigned int(32) size;        // Total size including header
    unsigned int(32) type;        // 4-character code

    // If size == 1, extended size follows:
    unsigned int(64) largesize;

    // If type == 'uuid', UUID follows:
    unsigned int(8)[16] usertype;
}
```

**Size Interpretation:**
- `size = 0` → Box extends to end of file (only valid for last box)
- `size = 1` → 64-bit `largesize` field present
- `size = [8...N]` → Actual size in bytes

#### FullBox (extends Box)

```
aligned(8) class FullBox extends Box {
    unsigned int(8) version;      // Typically 0 or 1
    bit(24) flags;                // Box-specific flags
}
```

**Header Sizes:**
- Basic box: 8 bytes
- Basic box with largesize: 16 bytes
- FullBox: 12 bytes (8 + version + flags)
- FullBox with largesize: 20 bytes

### 2.9 Matrix Transformation (tkhd)

**Type:** 9×int32 (36 bytes total)

**Format:** 3×3 matrix in row-major order, using 16.16 fixed-point

**Structure:**
```
[ a  b  u ]
[ c  d  v ]
[ tx ty w ]
```

**Default Identity Matrix (no transformation):**
```
[ 0x00010000  0x00000000  0x00000000 ]  // a=1.0, b=0, u=0
[ 0x00000000  0x00010000  0x00000000 ]  // c=0, d=1.0, v=0
[ 0x00000000  0x00000000  0x40000000 ]  // tx=0, ty=0, w=1.0
```

**90° Rotation (counterclockwise):**
```
[ 0x00000000  0x00010000  0x00000000 ]  // a=0, b=1.0, u=0
[ 0xFFFF0000  0x00000000  0x00000000 ]  // c=-1.0, d=0, v=0
[ 0x00000000  0x00000000  0x40000000 ]  // tx=0, ty=0, w=1.0
```

**Note:** While ISO 14496-12 specifies unity matrix only, iPhone and other devices use matrix rotation. Handle both cases.

---

## 3. Audio Property Extraction Path

This section documents the **complete** navigation path to extract all audio properties.

### 3.1 File Brand and Compatibility

**Path:** `ftyp` (File Type Box)

**Location:** First box in file (typically)

**Extract:**
- `major_brand` (4 bytes) - Primary file type
- `minor_version` (4 bytes) - Version
- `compatible_brands[]` - Array of compatible brands

**Common Audio Brands:**
- `M4A ` - iTunes audio
- `M4B ` - iTunes audiobook
- `mp41` - MP4 v1
- `mp42` - MP4 v2
- `isom` - ISO Base Media
- `iso2` - ISO Base Media v2
- `qt  ` - QuickTime

**Implementation Priority:** P0 (required for file detection)

### 3.2 Duration Extraction

Duration can be extracted from multiple locations. Implement all methods for robustness.

#### Method 1: Movie Header (mvhd)

**Path:** `moov` → `mvhd`

**Extract:**
```
timescale: uint32     // Time units per second (e.g., 1000 = milliseconds)
duration:  uint32/64  // Duration in timescale units (version-dependent)
```

**Calculation:**
```csharp
double durationSeconds = (double)duration / timescale;
```

**Pros:** Single location, applies to whole file
**Cons:** May not reflect edit lists

**Priority:** P0

#### Method 2: Media Header (mdhd) - Per Track

**Path:** `moov` → `trak` → `mdia` → `mdhd`

**Extract:**
```
timescale: uint32     // Media time units per second
duration:  uint32/64  // Duration in media timescale units
```

**Calculation:**
```csharp
double trackDurationSeconds = (double)duration / timescale;
```

**Pros:** Track-specific, accurate
**Cons:** Must identify correct audio track

**Priority:** P0

#### Method 3: Sample Table (stts) - Sample-Based

**Path:** `moov` → `trak` → `mdia` → `minf` → `stbl` → `stts`

**Extract:**
```
entry_count: uint32
For each entry:
    sample_count: uint32    // Number of consecutive samples
    sample_delta: uint32    // Duration of each sample in timescale units
```

**Calculation:**
```csharp
uint totalDuration = 0;
foreach (var entry in entries) {
    totalDuration += entry.sample_count * entry.sample_delta;
}
double durationSeconds = (double)totalDuration / mdhd.timescale;
```

**Pros:** Most accurate, frame-level precision
**Cons:** Requires parsing entire table

**Priority:** P1 (for validation/accuracy)

#### Method 4: Edit List Adjusted Duration

**Path:** `moov` → `trak` → `edts` → `elst`

**Notes:** Edit lists can add delays, trim start/end. Final duration should account for edits.

**Priority:** P1

**Recommendation:** Use `mdhd` duration as primary, validate against `mvhd`, optionally calculate from `stts` for accuracy.

### 3.3 Sample Rate Extraction

#### Path for AAC (mp4a)

**Path:** `moov` → `trak` → `mdia` → `minf` → `stbl` → `stsd` → `mp4a` → `esds`

**Extract from esds (Elementary Stream Descriptor):**

The `esds` box contains:
- ES Descriptor
  - Decoder Config Descriptor
    - **Audio Specific Config** ← Sample rate here

**Audio Specific Config Structure:**
```
Bits 0-4:   objectType (5 bits)
Bits 5-8:   samplingFrequencyIndex (4 bits)
If samplingFrequencyIndex == 15:
    Bits 9-32:  samplingFrequency (24 bits explicit)
Else:
    Use lookup table
Bits 33-36: channelConfiguration (4 bits)
```

**Sampling Frequency Index Lookup Table:**
```csharp
private static readonly int[] SamplingFrequencies = {
    96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050,
    16000, 12000, 11025, 8000, 7350, 0, 0, 0 // explicit in stream
};
```

**Priority:** P0

#### Path for ALAC (alac)

**Path:** `moov` → `trak` → `mdia` → `minf` → `stbl` → `stsd` → `alac` → `alac` (magic cookie)

**ALAC Magic Cookie Structure (36 bytes):**
```
Offset  Size  Field
0       4     Frame length (samples per frame, typically 4096)
4       4     Compatible version (0)
8       1     Sample size (bits: 16, 20, 24, 32)
9       1     Rice history mult (40)
10      1     Rice initial history (10)
11      1     Rice parameter limit (14)
12      1     Channels (1-8)
13      2     Max run (255)
15      4     Max coded frame size (0 = unknown)
19      4     Average bitrate (0 = unknown)
23      4     Sample rate (32-bit, big-endian)
```

**Extract:**
```csharp
byte[] magicCookie = ReadAlacMagicCookie(36);
int sampleRate = (magicCookie[23] << 24) | (magicCookie[24] << 16) |
                 (magicCookie[25] << 8) | magicCookie[26];
```

**Priority:** P0

#### Fallback: Audio Sample Entry

**Path:** `moov` → `trak` → `mdia` → `minf` → `stbl` → `stsd` → `mp4a`/`alac`

**AudioSampleEntry Structure:**
```
Offset  Size  Field
0       6     Reserved (0)
6       2     Data reference index
8       8     Reserved (0)
16      2     Channel count
18      2     Sample size (bits)
20      4     Reserved (0)
24      4     Sample rate (16.16 fixed-point)
```

**Extract:**
```csharp
uint32 sampleRateFixed = ReadUInt32BigEndian(offset + 24);
double sampleRate = (sampleRateFixed >> 16) + ((sampleRateFixed & 0xFFFF) / 65536.0);
```

**Note:** This is often 0 or incorrect for AAC; prefer codec-specific boxes.

**Priority:** P1 (fallback only)

### 3.4 Channel Count and Configuration

#### From Audio Sample Entry

**Path:** `moov` → `trak` → `mdia` → `minf` → `stbl` → `stsd` → `mp4a`/`alac`

**Extract:**
```
channelcount: uint16  // Offset 16 in AudioSampleEntry
```

**Common Values:**
- 1 = Mono
- 2 = Stereo
- 6 = 5.1 surround
- 8 = 7.1 surround

**Priority:** P0

#### From AAC Audio Specific Config

**Extract from esds:**
```
channelConfiguration (4 bits):
0 = Defined in AOT Specific Config (not common)
1 = 1 channel (mono)
2 = 2 channels (stereo)
3 = 3 channels (L, R, C)
4 = 4 channels (L, R, C, rear)
5 = 5 channels (L, R, C, LS, RS)
6 = 6 channels (L, R, C, LFE, LS, RS) - 5.1
7 = 8 channels (L, R, C, LFE, LS, RS, Lsr, Rsr) - 7.1
```

**Priority:** P0

#### From ALAC Magic Cookie

**Extract:**
```csharp
byte channels = magicCookie[12]; // Offset 12, range 1-8
```

**Priority:** P0

#### From Apple Channel Layout (chan) - Optional

**Path:** `stsd` → `mp4a`/`alac` → `chan`

**Structure:** Describes precise speaker layout (beyond just count)

**Priority:** P1 (advanced)

### 3.5 Bit Depth (Sample Size)

#### From Audio Sample Entry

**Extract:**
```
samplesize: uint16  // Offset 18 in AudioSampleEntry
```

**Common Values:**
- 16 = 16-bit PCM
- 24 = 24-bit PCM
- 32 = 32-bit PCM

**Note:** For AAC, this is often 16 but doesn't reflect actual codec bit depth.

**Priority:** P0

#### From ALAC Magic Cookie

**Extract:**
```csharp
byte sampleSize = magicCookie[8]; // Offset 8: 16, 20, 24, or 32 bits
```

**Priority:** P0

### 3.6 Bitrate Calculation

#### Method 1: From esds (AAC)

**Path:** `esds` → DecoderConfigDescriptor

**Extract:**
```
maxBitrate: uint32   // Maximum bitrate in bps
avgBitrate: uint32   // Average bitrate in bps (0 = unknown)
```

**Priority:** P0

#### Method 2: From ALAC Magic Cookie

**Extract:**
```csharp
uint avgBitrate = ReadUInt32BigEndian(magicCookie, offset: 19); // 0 = unknown
```

**Priority:** P0

#### Method 3: Calculate from Sample Table (VBR)

**Path:** `stbl` → `stsz` (Sample Size Box) + `mdhd` (duration)

**Algorithm:**
```csharp
// Read all sample sizes
uint totalBytes = 0;
if (stsz.sample_size != 0) {
    // Constant size
    totalBytes = stsz.sample_size * stsz.sample_count;
} else {
    // Variable size
    foreach (var size in stsz.entry_sizes) {
        totalBytes += size;
    }
}

// Get duration from mdhd
double durationSeconds = (double)mdhd.duration / mdhd.timescale;

// Calculate average bitrate
int bitrate = (int)((totalBytes * 8) / durationSeconds);
```

**Pros:** Accurate for VBR, works when esds/alac don't specify
**Cons:** Requires parsing entire sample size table

**Priority:** P1

#### Method 4: From btrt Box (ISO)

**Path:** `stsd` → sample entry → `btrt`

**Extract:**
```
bufferSizeDB: uint32  // Decoder buffer size
maxBitrate:   uint32  // Maximum bitrate in bps
avgBitrate:   uint32  // Average bitrate in bps
```

**Priority:** P2 (rare)

**Recommendation:** Use esds/alac bitrate if available, otherwise calculate from sample table.

### 3.7 Codec Information

#### Identify Codec

**Path:** `stsd` → sample entry type (4-character code)

**Common Audio Codecs:**
- `mp4a` → Further check `esds.objectTypeIndication`:
  - 0x40 = AAC-LC (Low Complexity)
  - 0x66 = AAC Main
  - 0x67 = AAC LC
  - 0x68 = AAC SSR
  - 0x69 = AAC LTP
  - 0x6B = MP3
- `alac` → Apple Lossless
- `ac-3` → Dolby Digital (AC-3)
- `ec-3` → Dolby Digital Plus (E-AC-3)
- `Opus` → Opus
- `.mp3` → MP3 (ISO 14496-3 Amd 3)

**Priority:** P0

### 3.8 Complete Extraction Flowchart

```
1. Read ftyp → Verify it's an audio file (M4A, M4B, mp42, etc.)
2. Navigate to moov → mvhd → Extract global timescale
3. Find audio trak:
   a. Navigate to trak → mdia → hdlr
   b. Check handler_type == 'soun' (audio track)
4. Extract from mdhd:
   - timescale (media time units)
   - duration → calculate seconds
   - language code → decode to ISO 639-2
5. Navigate to minf → stbl → stsd
6. Read AudioSampleEntry:
   - channelcount (basic)
   - samplesize (basic)
   - Check sample entry type (mp4a, alac, etc.)
7. For AAC (mp4a):
   a. Navigate to esds box
   b. Parse Audio Specific Config:
      - Sample rate (from index or explicit)
      - Channel configuration
      - Object type (AAC-LC, etc.)
   c. Extract bitrate from DecoderConfigDescriptor
8. For ALAC (alac):
   a. Navigate to alac magic cookie
   b. Extract:
      - Sample rate (offset 23, 4 bytes)
      - Channels (offset 12, 1 byte)
      - Sample size (offset 8, 1 byte)
      - Average bitrate (offset 19, 4 bytes)
9. Optional: Calculate bitrate from stsz if not available
10. Optional: Check for edit lists (elst) to adjust duration
```

---

## 4. Metadata Atom Complete Reference

### 4.1 Metadata Container Structure

**Path:** `moov` → `udta` → `meta` → `ilst`

**Structure:**
```
meta (Metadata Box) - FullBox with hdlr
├── hdlr (Handler Box) - handler_type = 'mdir'
├── ilst (Metadata Item List)
│   ├── ©nam (Title)
│   │   └── data (Data Atom)
│   ├── ©ART (Artist)
│   │   └── data (Data Atom)
│   └── ... (other metadata atoms)
└── keys (optional, for custom metadata)
```

### 4.2 Data Atom Structure

Every metadata item in `ilst` contains one or more `data` atoms:

```
Box Header:
    size: uint32
    type: 'data'
Data Atom:
    version: uint8 (typically 0)
    flags: uint24 (type indicator)
    reserved: uint32 (locale, typically 0)
    value: byte[] (format depends on flags)
```

### 4.3 Well-Known Data Type Indicators (flags)

| Value | Name | Description | Example Usage |
|-------|------|-------------|---------------|
| 0 | Implicit/Binary | Raw binary data, type context-dependent | Reserved |
| 1 | UTF-8 | UTF-8 text without null terminator | Title, Artist, Album |
| 2 | UTF-16 | UTF-16 BE text | Rarely used |
| 13 | JPEG | JPEG image data | Album artwork (covr) |
| 14 | PNG | PNG image data | Album artwork (covr) |
| 15 | URL | Absolute URL in UTF-8 | Podcast URL |
| 16 | Duration | Duration in milliseconds (32-bit int) | Rarely used |
| 17 | DateTime | Seconds since Jan 1, 1904 (32 or 64-bit) | Rarely used |
| 21 | Integer | Signed big-endian integer (1, 2, 3, 4, or 8 bytes) | Tempo, ratings |
| 22 | RIAA_PA | RIAA Parental Advisory (8-bit: -1=no, 0=unspecified, 1=yes) | Content rating |
| 23 | UPC | Universal Product Code (UTF-8 text) | Barcode |
| 24 | BMP | Windows BMP image | Artwork (rare) |
| 27 | ISRC | ISRC code | International Standard Recording Code |

### 4.4 Standard iTunes Metadata Atoms (Text)

All use 4-byte codes, most are © (0xA9) prefixed.

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `©nam` | Title | UTF-8 | Track/song title |
| `©ART` | Artist | UTF-8 | Primary artist(s) |
| `©alb` | Album | UTF-8 | Album title |
| `©gen` | Genre | UTF-8 | Genre (prefer over gnre) |
| `©day` | Year/Date | UTF-8 | Release date (YYYY or YYYY-MM-DD) |
| `©cmt` | Comment | UTF-8 | Comments |
| `©lyr` | Lyrics | UTF-8 | Song lyrics (can be very long) |
| `©wrt` | Composer | UTF-8 | Composer/songwriter |
| `©grp` | Grouping | UTF-8 | Grouping/content group |
| `©too` | Encoder | UTF-8 | Encoding tool |
| `cprt` | Copyright | UTF-8 | Copyright notice |
| `desc` | Description | UTF-8 | Short description |
| `ldes` | Long Description | UTF-8 | Detailed description |

**Priority:** P0

### 4.5 Album Artist and Compilation

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `aART` | Album Artist | UTF-8 | Album artist (may differ from track artist) |
| `cpil` | Compilation | Boolean (1 byte) | 1 = part of compilation, 0 = not |

**cpil values:** 0x00 = No, 0x01 = Yes

**Priority:** P0

### 4.6 Track and Disc Numbers (Tuple Integers)

| Atom Code | Name | Type | Structure |
|-----------|------|------|-----------|
| `trkn` | Track Number | Integer | 8 bytes: [0][0][track][total_tracks] (uint16 each) |
| `disk` | Disc Number | Integer | 8 bytes: [0][0][disc][total_discs] (uint16 each) |

**Structure Detail:**
```
data atom (type = 0 or 21):
    Byte 0-1: Reserved (0x0000)
    Byte 2-3: Track/Disc number (uint16 big-endian)
    Byte 4-5: Total tracks/discs (uint16 big-endian)
    Byte 6-7: Reserved (0x0000) - optional
```

**Example:**
- Track 3 of 12: `00 00 00 03 00 0C 00 00`
- Disc 1 of 2: `00 00 00 01 00 02 00 00`

**Priority:** P0

### 4.7 Integer Metadata

| Atom Code | Name | Type | Range | Description |
|-----------|------|------|-------|-------------|
| `tmpo` | BPM (Tempo) | uint16 | 1-65535 | Beats per minute |
| `rtng` | Content Rating | uint8 | 0-4 | 0=None, 1=Explicit, 2=Clean, 4=Explicit (old) |
| `stik` | Media Kind | uint8 | See table | Type of media (song, audiobook, etc.) |
| `hdvd` | HD Video | uint8 | 0-2 | 0=No, 1=720p, 2=1080p (video files) |
| `pcst` | Podcast | uint8 | 0-1 | 0=No, 1=Yes (read-only flag) |
| `pgap` | Gapless Playback | uint8 | 0-1 | 0=No, 1=Yes |
| `shwm` | Show Movement | uint8 | 0-1 | 0=No, 1=Yes (classical music) |

**stik (Media Kind) Values:**
- 0 = Movie
- 1 = Normal (music)
- 2 = Audiobook
- 5 = Whacked Bookmark
- 6 = Music Video
- 9 = Short Film
- 10 = TV Show
- 11 = Booklet
- 14 = Ringtone
- 21 = Podcast

**Priority:** P0 (tmpo, pgap), P1 (others)

### 4.8 Movement/Work Metadata (Classical Music)

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `©mvn` | Movement Name | UTF-8 | Name of movement |
| `©mvi` | Movement Index | uint16 | Movement number |
| `©mvc` | Movement Count | uint16 | Total movements in work |
| `©wrk` | Work Name | UTF-8 | Name of larger work |

**Priority:** P1

### 4.9 Sort Order Atoms

Used for alphabetical sorting, ignoring articles like "The".

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `soal` | Sort Album | UTF-8 | Album sort order |
| `soaa` | Sort Album Artist | UTF-8 | Album artist sort order |
| `soar` | Sort Artist | UTF-8 | Artist sort order |
| `soco` | Sort Composer | UTF-8 | Composer sort order |
| `sonm` | Sort Name | UTF-8 | Title sort order |
| `sosn` | Sort Show | UTF-8 | TV show sort order |

**Example:**
- Artist: "The Beatles"
- Sort Artist: "Beatles, The"

**Priority:** P1

### 4.10 Podcast-Specific Atoms

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `pcst` | Podcast Flag | uint8 | 1 = is podcast (read-only) |
| `purl` | Podcast URL | UTF-8 | Podcast feed URL |
| `egid` | Episode GUID | UTF-8 | Unique episode identifier |
| `catg` | Category | UTF-8 | Podcast category |
| `keyw` | Keywords | UTF-8 | Podcast keywords |
| `purd` | Purchase Date | UTF-8 | Purchase date (UTC string) |

**Priority:** P1

### 4.11 TV Show Metadata

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `tvsh` | TV Show Name | UTF-8 | Name of TV show |
| `tven` | TV Episode ID | UTF-8 | Episode identifier |
| `tvnn` | TV Network | UTF-8 | Broadcasting network |
| `tves` | TV Episode Number | uint32 | Episode number |
| `tvsn` | TV Season | uint32 | Season number |

**Priority:** P2 (audio files rarely use these)

### 4.12 Artwork (Cover Art)

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `covr` | Cover Art | JPEG/PNG | Album artwork image(s) |

**Structure:**
- Can contain multiple `data` atoms (front cover, back cover, etc.)
- Each data atom has type indicator 13 (JPEG) or 14 (PNG)
- No size limit specified, but typically ≤ 2048×2048 pixels

**Example:**
```
covr
├── data (type=13, JPEG image bytes)
└── data (type=14, PNG image bytes)
```

**Priority:** P0

### 4.13 iTunes Internal IDs (Integer)

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `cnID` | iTunes Catalog ID | uint32 | Apple Music catalog ID |
| `atID` | Album ID | uint32 | iTunes album ID |
| `plID` | Playlist ID | uint64 | iTunes playlist ID |
| `geID` | Genre ID | uint32 | iTunes genre ID |
| `sfID` | iTunes Store Country | uint32 | Store front ID |
| `cmID` | Composer ID | uint32 | iTunes composer ID |
| `akID` | Account Kind | uint8 | Account type |

**Priority:** P2 (informational, typically not user-editable)

### 4.14 Additional Metadata Atoms

| Atom Code | Name | Type | Description |
|-----------|------|------|-------------|
| `©pub` | Publisher | UTF-8 | Record label/publisher |
| `©enc` | Encoded By | UTF-8 | Person/org who encoded |
| `©swr` | Software | UTF-8 | Encoding software |
| `©src` | Source | UTF-8 | Source credit |
| `©fmt` | Format | UTF-8 | Format description |
| `©inf` | Information | UTF-8 | Additional information |
| `©req` | Requirements | UTF-8 | System requirements |
| `©mak` | Make | UTF-8 | Device make (camera, etc.) |
| `©mod` | Model | UTF-8 | Device model |
| `©PRD` | Producer | UTF-8 | Producer credit |
| `©dir` | Director | UTF-8 | Director (video) |
| `©cpy` | Copyright | UTF-8 | Copyright info (duplicate of cprt) |
| `©ope` | Original Artist | UTF-8 | Original performer |
| `©com` | Comment | UTF-8 | Alternate comment field |
| `apID` | Apple ID | UTF-8 | Account email |
| `akID` | Account Type | uint8 | iTunes account kind |
| `ownr` | Owner | UTF-8 | Owner name |
| `xid ` | Vendor ID | UTF-8 | Vendor identifier |

**Priority:** P2

### 4.15 Legacy/Deprecated Atoms

| Atom Code | Name | Type | Status | Notes |
|-----------|------|------|--------|-------|
| `gnre` | Genre (ID3v1) | uint16 | Deprecated | Use ©gen instead; ID3v1 genre codes 0-255 |
| `©gen` | Genre (old) | UTF-8 | Prefer ©gen | Some files use ©gen vs gnre |
| `rate` | Rating | UTF-8 | QuickTime | User rating (not standard) |
| `©xyz` | GPS | UTF-8 | Rare | GPS coordinates |

**Priority:** P2 (read for compatibility)

### 4.16 Freeform Metadata (Custom Tags)

**Atom Code:** `----` (four hyphens, 0x2D2D2D2D)

**Structure:**
```
---- (Freeform box)
├── mean (Reverse DNS domain)
│   └── data (type=1, UTF-8 string, typically "com.apple.iTunes")
├── name (Tag name)
│   └── data (type=1, UTF-8 string, e.g., "SCRIPT" or "MusicBrainz Artist Id")
└── data (Actual value)
    └── data (type=1 or other, value)
```

**Common Patterns:**
- **mean:** `com.apple.iTunes` (most common)
- **mean:** `org.musicbrainz` (MusicBrainz tags)
- **mean:** `com.pilabor.tone` (third-party apps)

**Example Freeform Tags:**
- `----:com.apple.iTunes:SCRIPT` = Script/language name
- `----:com.apple.iTunes:BARCODE` = UPC/EAN barcode
- `----:org.musicbrainz:Artist Id` = MusicBrainz artist UUID
- `----:org.musicbrainz:Album Id` = MusicBrainz release UUID
- `----:org.musicbrainz:Track Id` = MusicBrainz recording UUID

**Registration:**
```csharp
// Mutagen-style registration
RegisterFreeformKey("musicbrainz_artistid", "MusicBrainz Artist Id", "org.musicbrainz");
```

**Priority:** P1 (MusicBrainz and common tags), P2 (arbitrary custom tags)

### 4.17 Chapter Markers (Audiobooks)

**Path:** `moov` → `udta` → `chpl`

**Structure:**
```
chpl (Chapter List) - FullBox
    version: uint8
    flags: uint24
    unknown: uint32 (always 0)
    chapter_count: uint8
    For each chapter:
        timestamp: uint64 (100-nanosecond units since start)
        title_length: uint8
        title: UTF-8 string (not null-terminated)
```

**Timestamp Conversion:**
```csharp
double seconds = timestamp / 10_000_000.0;  // 100ns to seconds
```

**Alternative:** Chapters may also be stored as timed metadata tracks (handler_type = 'text').

**Priority:** P1 (important for M4B audiobooks)

---

## 5. Edge Cases and Compatibility Matrix

### 5.1 QuickTime vs. MP4 vs. iTunes Differences

| Feature | QuickTime (.mov) | ISO MP4 (.mp4) | iTunes (.m4a/.m4b) |
|---------|------------------|----------------|---------------------|
| **ftyp Box** | Optional (or `qt  ` brand) | Required (`mp41`, `mp42`, `isom`) | Required (`M4A `, `M4B `) |
| **Metadata** | udta → various atoms | meta (ISO) or udta (QT-style) | udta → meta → ilst (iTunes-style) |
| **Timecode Track** | Supported | Not supported | Not supported |
| **Edit Lists** | Common | Less common | Rare (used for gapless) |
| **Matrix Rotation** | Deprecated in ISO | Unity matrix only (ISO) | Used by iPhone (non-standard) |
| **Language Codes** | ISO 639-2 (mdhd) | ISO 639-2 or BCP-47 (elng) | ISO 639-2 (mdhd) |
| **Chapters** | Multiple formats | Timed text track | udta → chpl (simple format) |
| **DRM** | None (typically) | CENC (pssh) | FairPlay (sinf/schi/drms) |

### 5.2 Version Differences

#### mvhd/mdhd/tkhd Version

- **Version 0:** Uses `uint32` for creation_time, modification_time, duration
- **Version 1:** Uses `uint64` for the same fields (for dates after 2040 or very long files)

**Handling:**
```csharp
if (version == 0) {
    creation_time = ReadUInt32();
    modification_time = ReadUInt32();
    timescale = ReadUInt32();
    duration = ReadUInt32();
} else { // version 1
    creation_time = ReadUInt64();
    modification_time = ReadUInt64();
    timescale = ReadUInt32();
    duration = ReadUInt64();
}
```

#### stsd Version

- **Version 0:** Standard audio/video sample entries
- **Version 1:** Extended for some formats (rare)

### 5.3 Deprecated Atoms Still in Use

| Atom | Status | Still Found In | Replacement |
|------|--------|----------------|-------------|
| `gnre` | Deprecated | Old iTunes files | `©gen` (text genre) |
| `wave` | Legacy | Old QuickTime audio | Direct esds/codec box |
| Profile atoms | Deprecated | Very old MP4 | Not used |
| `iods` | Rarely used | Old MPEG-4 files | Individual track configs |
| Matrix rotation | Non-standard (ISO) | iPhone videos | Should be unity matrix |

### 5.4 Non-Standard But Common Atoms

| Atom | Standard | Found In | Purpose |
|------|----------|----------|---------|
| `chpl` | Nero/iTunes | Audiobooks (M4B) | Chapter markers (simple format) |
| `WLOC` | QuickTime | Old QuickTime | Window location (obsolete) |
| `chan` | Apple | ALAC/AAC | Detailed channel layout |
| `uuid` (various) | ISO extension | Various | Vendor-specific data |
| Freeform `----` | iTunes convention | Tagged audio | Custom metadata |

### 5.5 DRM Detection and Handling

#### FairPlay DRM (Apple)

**Detection Path:** `stsd` → sample entry → `sinf` → `schi`

**Indicators:**
- Presence of `sinf` (Protection Scheme Information Box)
- `schm` box with scheme_type = `'itun'` or similar
- `drms` atom in user data

**Action:** Skip file (cannot read encrypted data)

**Priority:** P2

#### Common Encryption (CENC)

**Detection Path:** Root level or `moov` → `pssh` (Protection System Specific Header)

**Indicators:**
- `pssh` box with system_id for Widevine, PlayReady, etc.
- `tenc` (Track Encryption) boxes in sample entries

**Action:** Skip file (DRM protected)

**Priority:** P3

#### Implementation

```csharp
bool IsProtected(Mp4File file) {
    // Check for pssh boxes
    if (file.HasBox("pssh")) return true;

    // Check for sinf in sample entries
    foreach (var trak in file.Tracks) {
        if (trak.SampleEntry.HasBox("sinf")) return true;
    }

    return false;
}
```

### 5.6 Gapless Playback

iTunes uses edit lists and custom atoms for gapless playback:

**Atoms Involved:**
- `edts` → `elst` (Edit List) - Trims encoder delay and padding
- `pgap` in metadata - Gapless flag
- iTunes-specific: iTunSMPB (freeform metadata with precise sample counts)

**iTunSMPB Format:**
```
----:com.apple.iTunes:iTunSMPB
Value: "00000000 00000840 000001C0 0000000000B3B000 00000000 00000000 00000000 00000000"
       [padding] [delay]  [padding][total_samples] [reserved...]
```

**Priority:** P1 (important for accurate playback)

### 5.7 Unusual Sample Rates

While 44100 Hz and 48000 Hz are standard, some files use:
- 88200 Hz, 96000 Hz (high-resolution audio)
- 22050 Hz, 11025 Hz (low-quality audio)
- 8000 Hz (telephony)

**Handling:** Support all rates in the AAC frequency table and arbitrary rates (index 15).

### 5.8 Multi-Track Files

Some MP4 files contain multiple audio tracks:
- Multiple languages
- Commentary tracks
- Stereo + 5.1 versions

**Handling:**
- Iterate all tracks with `hdlr.handler_type == 'soun'`
- Expose all tracks to user
- Default to first enabled track (`tkhd` flags & 0x0001)

**Priority:** P1

### 5.9 Large Files (64-bit Support)

**Indicators:**
- Box size == 1 (64-bit largesize present)
- `co64` instead of `stco` (64-bit chunk offsets)
- Version 1 boxes (64-bit durations)

**Requirement:** Full 64-bit file I/O support

**Priority:** P1

### 5.10 Corrupted/Incomplete Files

**Common Issues:**
- moov box at end of file (normal for streaming write)
- Truncated mdat (incomplete download)
- Missing required boxes
- Invalid box sizes

**Handling:**
- Scan for moov if not at expected location
- Validate box size before reading
- Graceful degradation (extract what's available)

**Priority:** P1

---

## 6. Implementation Roadmap by Priority

### P0 (Must Have for v0.4.0) - **Core Audio Tagging**

**Goal:** Read and write basic iTunes-style metadata for M4A/M4B files.

#### File Format Support
- ✅ Read ftyp (file type detection)
- ✅ Read/write basic box structure
- ✅ Read/write FullBox (version + flags)
- ✅ Handle extended size (largesize when size==1)
- ✅ Navigate box hierarchy (moov → udta → meta → ilst)

#### Audio Properties (Read-Only)
- ✅ Extract duration (from mvhd and mdhd)
- ✅ Extract sample rate:
  - AAC: from esds → Audio Specific Config
  - ALAC: from alac magic cookie
- ✅ Extract channel count (from AudioSampleEntry and codec config)
- ✅ Extract bit depth (from AudioSampleEntry)
- ✅ Extract bitrate:
  - AAC: from esds DecoderConfigDescriptor
  - ALAC: from magic cookie
  - Fallback: calculate from stsz
- ✅ Detect codec (mp4a, alac)
- ✅ Identify AAC sub-type (AAC-LC, etc.)

#### Core Metadata (Read/Write)
- ✅ Text fields:
  - ©nam (Title)
  - ©ART (Artist)
  - ©alb (Album)
  - aART (Album Artist)
  - ©gen (Genre)
  - ©day (Year)
  - ©cmt (Comment)
  - ©lyr (Lyrics)
  - ©wrt (Composer)
  - ©grp (Grouping)
- ✅ Track/disc numbers (trkn, disk)
- ✅ Cover art (covr) - JPEG and PNG
- ✅ Boolean flags (cpil, pgap)
- ✅ Integer fields (tmpo)

#### Critical Infrastructure
- ✅ Handle both 32-bit and 64-bit boxes
- ✅ Preserve unknown boxes when rewriting
- ✅ Update box sizes correctly on write
- ✅ Handle iTunes-style metadata (data atoms with type indicators)
- ✅ Support UTF-8 text encoding

**Estimated Effort:** 4-6 weeks
**Deliverable:** Basic M4A tagging comparable to FLAC/Ogg support

---

### P1 (Should Have) - **Extended Metadata & Robustness**

**Goal:** Full metadata compatibility, advanced features, multi-track support.

#### Extended Metadata (Read/Write)
- ✅ Sort order atoms (soal, soaa, soar, soco, sonm)
- ✅ Podcast metadata (purl, egid, catg, keyw, purd)
- ✅ Movement/work metadata (©mvn, ©mvi, ©mvc, ©wrk)
- ✅ Additional text fields (©pub, ©enc, etc.)
- ✅ MusicBrainz freeform tags
- ✅ Common freeform custom tags (----:com.apple.iTunes:*)

#### Chapter Support (M4B)
- ✅ Read chpl (chapter list) atoms
- ✅ Write chpl atoms
- ✅ Convert to/from standardized chapter format

#### Advanced Audio Properties
- ✅ Gapless playback detection (pgap, edts/elst)
- ✅ iTunSMPB parsing (precise gapless info)
- ✅ Multi-track file support (list all audio tracks)
- ✅ Language metadata (mdhd language codes, decode to ISO 639-2)
- ✅ Extended language (elng BCP-47 tags)

#### Robustness
- ✅ Handle moov at end of file
- ✅ Gracefully handle truncated files
- ✅ Validate box sizes and hierarchies
- ✅ Support files > 4GB (co64, version 1 boxes)
- ✅ Preserve edit lists when present

#### Codec Expansion
- ✅ Dolby Digital (ac-3, ec-3) detection
- ✅ MP3-in-MP4 detection
- ✅ Extract codec-specific config for all types

**Estimated Effort:** 3-4 weeks
**Deliverable:** Professional-grade M4A/M4B tagger

---

### P2 (Nice to Have) - **Advanced Features & Edge Cases**

**Goal:** Handle uncommon formats, advanced use cases.

#### Additional Codecs
- ⬜ Opus (dOps box)
- ⬜ AMR (sawb, samr)
- ⬜ Other registered audio codecs

#### Advanced Metadata
- ⬜ TV show metadata (tvsh, tven, tvnn, tves, tvsn)
- ⬜ Video metadata (stik values for video types)
- ⬜ All remaining © atoms
- ⬜ iTunes Store IDs (cnID, atID, plID, etc.) - read-only
- ⬜ Legacy metadata (gnre ID3v1 genre, etc.)

#### Channel Layout
- ⬜ Parse chan (channel layout) box
- ⬜ Map to standardized channel configurations

#### DRM Detection
- ⬜ Detect FairPlay (sinf/schi/drms)
- ⬜ Detect CENC (pssh)
- ⬜ Return "file is protected" error gracefully

#### Matrix Transformations
- ⬜ Read tkhd matrix (for rotation metadata)
- ⬜ Interpret rotation values (for completeness)

#### Rare Box Types
- ⬜ btrt (bit rate box)
- ⬜ Compact sample size (stz2)
- ⬜ Shadow sync samples (stsh)
- ⬜ Sample degradation priority (stdp)

**Estimated Effort:** 2-3 weeks
**Deliverable:** Comprehensive format support

---

### P3 (Future/Out of Scope) - **Specialized Features**

**Goal:** Video support, streaming, editing features.

#### Out of Scope for Audio Tagger
- ⬜ Video track parsing (vmhd, visual sample entries)
- ⬜ Video codec support (avc1, hvc1, etc.)
- ⬜ Movie fragments (moof, traf, trun) - for DASH/HLS
- ⬜ Fragment indexing (sidx, ssix)
- ⬜ Hint tracks (hmhd)
- ⬜ Subtitle/text tracks
- ⬜ HEIF/HEIC image support (different format family)
- ⬜ Timed metadata tracks
- ⬜ 3D video metadata
- ⬜ Sample groups (sbgp, sgpd)
- ⬜ Sub-sample information (subs)
- ⬜ Sample auxiliary info (saiz, saio)

#### Advanced Editing (Future)
- ⬜ Add/remove tracks
- ⬜ Modify edit lists
- ⬜ Re-encode audio
- ⬜ Optimize file layout (moov before mdat)

**Note:** These are beyond the scope of an audio tagging library. Users needing video support should use specialized tools.

---

## Authoritative Sources

This roadmap is compiled from the following authoritative sources:

### Official Specifications
1. [ISO/IEC 14496-12:2015](https://www.iso.org/standard/68960.html) - ISO Base Media File Format (ISOBMFF)
2. [ISO/IEC 14496-14:2020](https://www.iso.org/standard/79110.html) - MP4 File Format
3. [MP4 Registration Authority](https://mp4ra.org/) - Official registry of box types, codecs, brands
4. [Apple QuickTime File Format Specification](https://developer.apple.com/library/archive/documentation/QuickTime/QTFF/QTFFPreface/qtffPreface.html) - Official Apple documentation
5. [RFC 8486](https://www.opus-codec.org/docs/opus_in_isobmff.html) - Opus in ISOBMFF

### Implementation References
6. [Mutagen](https://mutagen.readthedocs.io/en/latest/api/mp4.html) - Python MP4 library (well-documented)
7. [AtomicParsley](https://atomicparsley.sourceforge.net/) - Command-line MP4 metadata tool
8. [mp4v2 iTunes Metadata Wiki](https://github.com/sergiomb2/libmp4v2/wiki/iTunesMetadata) - Reverse-engineered iTunes tags
9. [Bento4](https://www.bento4.com/) - Professional MP4 library and tools
10. [FFmpeg isom.c](https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/isom.c) - Production MP4 parser

### Technical Articles
11. [Auphonic Blog: MPEG-4 iTunes-style Metadata](https://auphonic.com/blog/2012/02/18/podcast-comparison-part-4-mpeg-4-itunes-style-metadata-aac-audio-m4a-mp4/)
12. [Understanding AAC - MultimediaWiki](https://wiki.multimedia.cx/index.php/Understanding_AAC)
13. [ALAC Magic Cookie Description](https://github.com/macosforge/alac/blob/master/ALACMagicCookieDescription.txt)
14. [Language tagging in GPAC](https://github.com/gpac/gpac/wiki/Language-tagging-in-GPAC)

### Format Documentation
15. [Library of Congress - ISO Base Media File Format](https://www.loc.gov/preservation/digital/formats/fdd/fdd000079.shtml)
16. [Library of Congress - MPEG-4 File Format](https://www.loc.gov/preservation/digital/formats/fdd/fdd000155.shtml)
17. [QuickTime File Format 2001 PDF](https://developer.apple.com/standards/qtff-2001.pdf)

---

## Notes for Implementation

### Clean Room Development
- **Do NOT reference TagLib# source code** for MP4 implementation
- **Do reference** format specifications, official docs, and test files
- **OK to design** similar APIs (APIs aren't copyrightable)
- **OK to use** existing MP4 files as test inputs

### Testing Strategy
1. **Unit tests** for box parsing (ftyp, mvhd, mdhd, stsd, esds, ilst, etc.)
2. **Integration tests** with real M4A/M4B files
3. **Round-trip tests** (read → modify → write → verify)
4. **Compatibility tests** with iTunes, VLC, foobar2000
5. **Edge case tests** (large files, DRM detection, corrupted data)

### Performance Considerations
- Use `Span<byte>` for parsing (avoid allocations)
- Memory-map large files if possible
- Lazy-load metadata (don't parse entire file upfront)
- Cache frequently accessed boxes (mvhd, mdhd, ilst)

### Error Handling
- **Required boxes missing:** Return error, don't crash
- **Invalid box sizes:** Validate before reading
- **DRM detected:** Return "protected file" error
- **Truncated data:** Gracefully degrade (return what's available)

---

**End of Roadmap**
