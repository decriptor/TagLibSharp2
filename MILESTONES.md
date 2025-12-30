# TagLibSharp2 Milestone Map

*Synthesized from Audiophile, Dev PM, Audio Product, and Project Management perspectives*

---

## Current State Assessment

| Metric | Value | Notes |
|--------|-------|-------|
| **Tests Passing** | 1,964 | Solid foundation |
| **Source Files** | 70+ | Core + 6 formats |
| **Formats Complete** | 6 of 22 | MP3, FLAC, OGG Vorbis, Opus, WAV, AIFF (all read+write) |
| **Format Coverage** | ~27% | Significant work remaining |
| **Core Infrastructure** | ✅ 100% | Tag, BinaryData, IFileSystem, Picture |

---

## Revised Priority Order

Based on multi-perspective analysis, the original roadmap is reordered:

| Original Priority | Revised Priority | Format | Rationale |
|-------------------|------------------|--------|-----------|
| P0-1 WAV | **✅ COMPLETE** | WAV | Full read/write with RIFF INFO + ID3v2 + bext (BWF) |
| P0-3 MP4/M4A | **P0-1 MP4/M4A** | ⬆️ Moved UP | Apple ecosystem is massive; 25% of lossless libraries |
| P0-7 VBR Headers | **✅ COMPLETE** | VBR | Xing/VBRI header parsing for accurate MP3 duration |
| P0-6 APE Tag | **P0-3 APE Tag** | Same | Blocks WavPack/Musepack/Monkey's Audio |
| P0-5 Opus | **✅ COMPLETE** | Opus | Full read/write with OpusHead + OpusTags |
| P0-2 AIFF | **✅ COMPLETE** | AIFF | Full read/write with FORM container + COMM + ID3 |
| P0-4 DSF | **P0-5 DSF** | ⬇️ Demoted | DSD is vocal minority, not market size |
| P1 Musepack | **SKIP** | ❌ Remove | Dead format (last release 2009) |

---

## Milestone Map

### Milestone 1: Technical Debt & Infrastructure
**Duration:** 1 week | **Status:** Mostly Complete

Fix blocking issues before new formats:

| Task | Effort | Status |
|------|--------|--------|
| Extract `EndianReader.cs` static class | 2h | 🔶 In BinaryData (adequate for now) |
| Extract `SyncsafeInteger.cs` static class | 1h | 🔶 In Id3v2Tag (adequate for now) |
| Create `ExtendedFloat.cs` (80-bit IEEE 754) | 4h | ✅ Complete |
| Fix DSD duration overflow (use double) | 1h | ❌ Not started (DSF not yet implemented) |
| Format detection factory | 4h | ❌ Not started |
| Complete IDisposable pattern | 4h | ❌ Not started |

**Exit Criteria:**
- All utility classes extracted and tested
- No integer overflow on DSD files
- Format detection working for existing formats

---

### Milestone 2: MP3/FLAC Write Support
**Duration:** 1-1.5 weeks | **Status:** ✅ Complete

Complete the formats we already read:

| Task | Effort | Dependencies |
|------|--------|--------------|
| MP3 ID3v2 write | 3d | ✅ Complete |
| MP3 ID3v1 write | 1d | ✅ Complete |
| MP3 VBR header parsing (Xing/VBRI) | 2d | ✅ Complete |
| FLAC metadata block write | 3d | ✅ Complete |
| FLAC padding management | 1d | ✅ Complete |
| Round-trip tests (MP3, FLAC) | 2d | ✅ Complete |

**Exit Criteria:**
- ✅ Read → Modify → Write → Read produces identical data
- ✅ MP3 duration accurate for VBR files
- ✅ Cross-tagger compatibility (foobar2000, Mp3tag)

---

### Milestone 3: MP4/M4A (Critical Path)
**Duration:** 1.5-2 weeks | **Status:** Not Started | **Complexity:** 10/10

Highest business value (Apple ecosystem):

| Task | Effort | Notes |
|------|--------|-------|
| Atom tree navigation | 3d | moov/udta/meta/ilst path |
| Standard atoms (©nam, ©ART, ©alb, etc.) | 2d | Text atoms |
| trkn/disk parsing | 1d | Track/disc number pairs |
| covr atom (cover art) | 1d | JPEG/PNG images |
| Freeform atoms (----/mean/name/data) | 2d | Custom fields |
| Audio properties from stsd/mvhd | 1d | Duration, sample rate |
| Round-trip tests | 2d | iTunes compatibility |

**Exit Criteria:**
- iTunes-tagged files read correctly
- Write operations don't break iTunes compatibility
- ALAC and AAC variants both work

**Risks:**
- Complex atom tree structure
- Extended size atoms (>4GB files)
- iTunes quirks (non-standard v2.3 sizes)

---

### Milestone 4: WAV & RIFF Infrastructure
**Duration:** 1 week | **Status:** ✅ Complete

Studio format + shared container:

| Task | Effort | Notes |
|------|--------|-------|
| RIFF container parser | 2d | ✅ Complete |
| RIFF INFO tags | 1d | ✅ Complete (INAM, IART, IPRD, etc.) |
| WAV fmt chunk parsing | 1d | ✅ Complete + WAVEFORMATEXTENSIBLE |
| WAV ID3v2 chunk support | 1d | ✅ Complete |
| WAV bext chunk (BWF) | 1d | ✅ Complete |
| WAV write support | 2d | ✅ Atomic writes |
| Round-trip tests | 1d | ✅ Complete |

**Exit Criteria:**
- ✅ WAV files with RIFF INFO tags read correctly
- ✅ WAV files with ID3v2 chunks read correctly
- ✅ Write operations use atomic writer

---

### 🎯 ALPHA RELEASE (v0.1.0) - ✅ RELEASED 2025-12-26
**Status:** Released

**Formats:** MP3, FLAC, OGG Vorbis, WAV, AIFF (5 formats with full read/write)

**Quality Bar:**
- [x] All 5 formats pass round-trip tests
- [x] 1,000+ tests
- [x] Cross-tagger compatibility verified
- [x] No known data-loss bugs
- [x] NuGet package published

### 🎯 v0.2.0 - ✅ RELEASED 2025-12-29

**Added:**
- ID3v2.2 legacy support (3-char frame IDs)
- ID3v2 unsynchronization and frame flags (compression, grouping)
- FLAC MD5 audio signature
- BWF (bext chunk) support for WAV
- WAVEFORMATEXTENSIBLE support
- Ogg CRC validation option
- Picture support for WAV and AIFF
- AIFF write support

### 🎯 v0.2.1 - ✅ RELEASED 2025-12-29

**Added:**
- Error context for file parsing (WavFileReadResult, AiffFileReadResult)
- Test coverage: Polyfills, OggCrc, Id3v1Genre (+51 tests)
- 1,939 total tests

---

### Milestone 5: Opus & APE Tag
**Duration:** 1 week | **Status:** Opus ✅ Complete, APE Tag Not Started

Modern lossy + infrastructure for P1 formats:

| Task | Effort | Notes |
|------|--------|-------|
| Opus OpusHead parsing | 1d | ✅ Complete |
| Opus OpusTags parsing | 1d | ✅ Complete |
| Opus R128 gain handling | 0.5d | ❌ Not started (OutputGain field available) |
| APE Tag v2 format | 3d | ❌ Not started |
| APE tag in MP3 | 1d | ❌ Not started |
| Round-trip tests | 1d | ✅ Opus complete |

**Exit Criteria:**
- ✅ Opus files read correctly
- ❌ APE tags read/write in isolation
- ❌ MP3 with APE+ID3v2+ID3v1 handled correctly

---

### Milestone 6: AIFF & DSF
**Duration:** 1 week | **Status:** AIFF ✅ Complete, DSF Not Started

Completing P0 formats:

| Task | Effort | Notes |
|------|--------|-------|
| AIFF FORM container | 1d | ✅ Complete |
| AIFF COMM chunk (80-bit float) | 1d | ✅ Complete (ExtendedFloat) |
| AIFF ID3 chunk | 1d | ✅ Complete |
| AIFF AIFC compression support | 0.5d | ✅ Complete |
| AIFF write support | 1d | ✅ Complete |
| DSF DSD/fmt chunks | 1d | ❌ Not started |
| DSF ID3v2 at offset | 1d | ❌ Not started |
| Round-trip tests | 1d | ✅ AIFF Complete |

**Exit Criteria:**
- ✅ AIFF sample rate parsed correctly
- ❌ DSF metadata at end of file works
- ❌ DSD duration calculation correct

---

### 🎯 BETA RELEASE (v0.5.0)
**Target:** Week 10-12 from start

**Formats:** All P0 (8 formats)

**Quality Bar:**
- [ ] All 8 P0 formats pass comprehensive tests
- [ ] >90% test coverage
- [ ] Zero memory leaks in stress tests
- [ ] Large file support verified (>4GB)
- [ ] Production Roon deployment (beta users)

---

### Milestone 7: P1 Extended Formats
**Duration:** 2-3 weeks | **Status:** Not Started

Legacy and niche formats:

| Format | Effort | Dependencies |
|--------|--------|--------------|
| WMA/ASF | 5-6d | None (standalone) |
| DFF | 1-2d | Read-only, no tags |
| WavPack | 3d | APE tags |
| OGG FLAC | 2d | OGG container exists |
| Speex | 1d | OGG container exists |

**Note:** Skip Musepack (dead format)

---

### Milestone 8: P2 Niche Formats
**Duration:** 1-2 weeks | **Status:** Not Started

Edge cases only if requested:

| Format | Effort | Notes |
|--------|--------|-------|
| TrueAudio | 2d | ID3v2/ID3v1 |
| Monkey's Audio | 2d | APE tags |
| Tracker formats (MOD/S3M/IT/XM) | 3d | Title only |

---

### Milestone 9: Polish & Release
**Duration:** 2-3 weeks | **Status:** Not Started

Production readiness:

| Task | Effort |
|------|--------|
| Performance optimization pass | 3d |
| Memory leak audit | 2d |
| TagLib# compatibility shim | 3d |
| Complete XML documentation | 3d |
| Migration guide | 2d |
| Example projects | 2d |
| 9 known spec issues fixed | 2d |

---

### 🎯 PRODUCTION RELEASE (v1.0.0)
**Target:** Week 18-20 from start

**Formats:** 22 total (or documented limitations)

**Quality Bar:**
- [ ] >95% test coverage
- [ ] Zero known critical bugs
- [ ] Real-world corpus tested (1,000+ files)
- [ ] 4+ weeks production Roon usage
- [ ] Community adoption (3+ projects)

---

## Timeline Summary

```
✅ COMPLETE: Technical Debt + MP3/FLAC Write + WAV/RIFF + AIFF
✅ v0.1.0 RELEASED: 2025-12-26 (MP3, FLAC, OGG Vorbis, WAV, AIFF)
✅ v0.2.0 RELEASED: 2025-12-29 (ID3v2.2, unsync, BWF, WAVEFORMATEXTENSIBLE)
✅ v0.2.1 RELEASED: 2025-12-29 (Error context, test coverage)

NEXT UP:
- MP4/M4A Implementation (critical path - Apple ecosystem)
- Opus & APE Tag
- DSF (DSD format)
         >>> BETA RELEASE (v0.5.0) <<<
- WMA/ASF + DFF + WavPack
- OGG FLAC + Speex + Bug Fixes
         >>> RELEASE CANDIDATE (v0.9.0) <<<
- P2 Niche Formats + Polish
         >>> PRODUCTION RELEASE (v1.0.0) <<<
```

---

## Key Decisions Made

### What We're Prioritizing

1. **MP4/M4A moved to P0-1** - Apple ecosystem is too large to ignore
2. **VBR Headers moved to P0-3** - Broken MP3 duration is unacceptable UX
3. **Classical metadata** - Work/Part/Movement hierarchy is a differentiator
4. **Both ReplayGain and R128** - Industry hasn't converged
5. **MusicBrainz IDs** - High adoption, critical for matching

### What We're Deprioritizing

1. **AIFF/DSF demoted within P0** - Smaller user bases than WAV/MP4
2. **Musepack SKIPPED** - Dead format (last release 2009)
3. **MQA detection** - MQA is dead, just read ENCODER tag naturally
4. **Tracker formats** - Niche retro community
5. **Video formats** - Outside music library scope

### Architecture Decisions

1. **Extract utility classes first** - EndianReader, SyncsafeInteger, ExtendedFloat
2. **RIFF container before WAV** - Reuse for AVI if needed later
3. **APE tag format before WavPack/Musepack** - Unlocks multiple formats
4. **Format detection factory early** - Better extensibility

---

## Risk Mitigation

| Risk | Severity | Mitigation |
|------|----------|------------|
| MP4 complexity | HIGH | Extra time allocated (2 weeks) |
| Cross-tagger quirks | MEDIUM | Test with real files from foobar2000, iTunes, Picard |
| Large file overflow | MEDIUM | Use double for DSD duration, long for offsets |
| Clean room contamination | CRITICAL | Reference specs only, not TagLib# source |
| Data loss on write | CRITICAL | AtomicFileWriter already implemented |

---

## Success Metrics

### Alpha Release (v0.1.0) - ✅ ACHIEVED
- ✅ 5 formats working (MP3, FLAC, OGG Vorbis, WAV, AIFF)
- ✅ 1,000+ tests passing (now 1,939)
- ✅ <10ms tag reading performance
- ✅ Zero data loss bugs

### Beta Release
- 8 P0 formats complete (need: MP4/M4A, Opus, DSF)
- Production Roon deployment (beta users)
- Cross-tagger compatibility verified

### Production Release
- 22 formats (or documented limitations)
- 4+ weeks production stability
- Community adoption

---

## Resource Estimate

| Phase | Developers | Duration |
|-------|------------|----------|
| Alpha (5 formats) | 1-2 | 6-8 weeks |
| Beta (8 formats) | 1-2 | 10-12 weeks |
| Production (22 formats) | 1-2 | 18-20 weeks |

**Parallelization:** With 2 developers, alpha could be 4-5 weeks.

---

*Last Updated: 2025-12-29 (v0.2.1)*
*Sources: Audiophile analysis, Dev PM analysis, Audio Product analysis, Project Management analysis*
