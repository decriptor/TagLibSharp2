# Implementation Guide

How to take a change from issue to a pull request that passes review in the first or second round.

Reviews here keep finding the same kinds of problem late: a field another reader still shows with the old
value, a sibling entry point the fix did not reach, a crafted file that crashes the process, a guard no test
pins. Reviewers find them with a small set of tools. This guide gives you those tools and tells you when to
run them, so you find these problems before a reviewer does.

The steps are in order. Do not skip one because the change looks small. Small changes are where twins hide.

- [(a) Behaviour survey before code](#a-behaviour-survey-before-code)
- [(b) Reach and twin table](#b-reach-and-twin-table)
- [(c) Library rules](#c-library-rules)
- [(d) Run the kit](#d-run-the-kit)
- [(e) Pre-review](#e-pre-review)
- [(f) Fix rounds](#f-fix-rounds)
- [(g) Sequencing](#g-sequencing)
- [Kit reference](#kit-reference)

The clean-room rules in [CLAUDE.md](../CLAUDE.md) apply throughout. Run other implementations only as black
boxes and compare their output. Never read their source. That includes TagLib#, ffmpeg, mutagen, TagLib,
mkvtoolnix and MP4v2.

## (a) Behaviour survey before code

Find out what real files hold and what real readers show before you decide what the library should do.

1. **Make sample files with each common writer** for the fields you touch. Name every writer you used.
   - All formats: ffmpeg (`-metadata`, `-f lavfi -i sine=...`), mutagen, exiftool.
   - MP4/M4A/MOV: also Apple tools (Music, `afconvert`, AVFoundation exports) and MP4Box.
   - Matroska: also mkvmerge/mkvpropedit.
   - MP3/ID3: also eyeD3, Mp3tag or Picard, and iTunes for iTunes-specific frames.
   - FLAC/Vorbis/Opus: also `metaflac`, `vorbiscomment` and `opustags`.
   - ASF/WMA: also Windows Media Player and Windows Explorer.

   Keep generated files in your scratch directory. Never commit media.
2. **Record what each reader shows** for every field you touch, before and after an edit. Use ffprobe,
   exiftool, mutagen, mediainfo, TagLib# (run as a black box) and AVFoundation on macOS.
   `tools/compare/readers.sh` prints the first four side by side.
3. **Decide each cell from the specification, with a citation.** The other readers can be wrong. When they
   disagree with the specification, follow the specification and say so. When the specification is silent,
   follow the majority of readers and say that too.
4. **Put the table in the PR as a design note:**

   | field | writer | on disk | ffprobe | exiftool | mutagen | mediainfo | TagLib# | we read | we write | spec |
   |-------|--------|---------|---------|----------|---------|-----------|---------|---------|----------|------|
   | title | ffmpeg 8 | `©nam` + keys `com.apple.quicktime.title` | new | new | new | new | new | keys item | both | QTFF "Metadata" |

## (b) Reach and twin table

Most late findings are the untested twin of something already fixed. Before you write code, list every
place the changed rule reaches. Fill in the last two columns as you go.

| site | how it is reached | fixed / not applicable (why) | pinning test |
|------|-------------------|------------------------------|--------------|
| `Mp4File.Read (ReadOnlySpan<byte>)` | span, sync | fixed | `Read_X_Y` |
| `Mp4File.Read (Stream, ...)` (windowed) | stream, sync | fixed | `ReadStream_X_Y` |
| `MediaFile.ReadAsync (path)` | path, async | fixed | ... |

Every table must cover:

- **Entry points.** Span, stream and path. Sync and async. `ReadFromFile`/`ReadFromFileAsync`. The windowed
  stream reader, which is a separate code path from the span reader. `MediaFile` detection, the extension
  fallback in `DetectFormat`, and `SupportedExtensions` where a format has one. A detection change must not
  take another format's files. Run the whole suite and the grid over the corpus to check this.
- **Sibling formats.** Every format that stores the same structure or field. ID3v2 lives in MP3, WAV, AIFF,
  DSF, DFF and MPEG-PS. APEv2 lives in MP3, Monkey's Audio, WavPack and Musepack. Vorbis comments live in
  FLAC, Ogg Vorbis, Opus and Ogg FLAC. EBML and ISO boxes nest the same way everywhere.
- **Every write path.** Each setter, null, `""`, `Clear()`, `CopyTo` (into itself, through a view, and into
  every other tag type) and assigning a whole tag (`file.Tag = ...`, `file.Id3v2Tag = null`).
- **Views.** `CombinedTag` and the per-format unified views (FLAC, MP3, AIFF and others) that forward tag
  members. A member added to `Tag` must be forwarded by every view.
- **Render and parse.** Whatever parse accepts, render must write back. Whatever render writes, parse must
  read back. Check both directions for every value shape: empty, absent, multi-value, longest, widest
  encoding.

**Tests are matrix or property tests over the table**, not one more ordering per review round. Use
`[DynamicData]` over {entry point} × {format} × {edit}, or over {message} × {arrival order}, so the next
row you add is tested without a new test method. See `NestingDepthTests` and `DepthCaseTests` for the
pattern.

## (c) Library rules

Every change keeps all of these rules. Reviewers check each one. Each rule below comes with an example of
how it was broken before.

1. **No recursion on depth the file controls.** Walk only the element or box paths the specification
   defines, and bound any nesting.
   *Past example:* a Matroska save walked the Cues recursively. A crafted 90 KB file crashed the process
   with a stack overflow on a title edit. Later, a cover save reached the same walker by a new path. The
   depth row in `DepthCases` exists so the next path is caught by a test, not a review.
2. **Every parse has a memory and work bound proportional to the bytes present.** Detection and
   `IsValidFormat` never copy the input. Pin bounds with an allocation or operation count
   (`Bounds`, `CountingStream`), never with wall-clock time.
   *Past examples:* a 43 KB MP4 made a chapter save allocate 1.9 GB, because one range was stored for each
   zero-length extent. Span detection copied the whole input when an AAC start and a trailing tag were both
   present. A 2-second time limit sat just under a quadratic cost and passed on fast machines.
3. **Malformed input returns a failure result.** It never throws or crashes out of Read, Parse, Detect or
   Save.
   *Past example:* a check lost its lower bound, and an MPEG stream shorter than 128 bytes made the span
   read throw. Both `Hostile.EveryTruncation` and `tools/fuzz` find this kind of bug in seconds.
4. **After a set or a clear, every reader shows the new value.** No stale duplicate copy survives anywhere
   in the file. Check ffprobe, exiftool, mutagen, TagLib#, mediainfo, and AVFoundation where it applies.
   *Past example:* an MP4 set wrote the iTunes atom but left the QuickTime `keys` item that ffmpeg had
   written for the same field. mutagen showed the new title and ffprobe showed the old one.
5. **Writing back the value read changes nothing, and null clears.** Where a no-op save was byte-identical
   before the change, it stays byte-identical.
   *Past example:* writing `Artist` back unchanged to an ID3v2 tag dropped every name after the first. An
   unchanged write-back of an ASF property deleted the copy that Windows reads from the Metadata Library.
6. **A value the caller did not change keeps its bytes and its meaning.**
   *Past example:* an AVI INFO save rewrote untouched non-UTF-8 text as UTF-8 of its Latin-1 misreading, and
   dropped the file's `CSET`.
7. **An empty EBML or ISO element means its default** (RFC 8794 §7.2). It does not mean the file is
   malformed.
   *Past example:* an empty `CueRefCluster` made every save that moved the Cues fail.
8. **Every refusal returns its own error message.** A later generic error must not mask it.
   *Past example:* a save refused an unknown-size Tracks with the message for a duplicate Tracks, so the
   caller looked for the wrong cause.

Two further rules come up in nearly every review:

- **Every guard you add turns a test red when you revert it.** Revert each guard by itself and run its
  tests. `tools/mutation.sh` (`dotnet stryker` on the changed files) does this mechanically.
- **The PR body is true at the head.** Test counts, commands, pinning-test names and claims such as "every
  reader shows" or "byte-identical" must match the code you push.

## (d) Run the kit

Run all six of these before you open the PR, and again before you push any fix. Paste the short results
into the PR body. Commands are in [Kit reference](#kit-reference).

1. **Before/after grid.** Run `tools/compare/grid.cs` over the corpus at the base and at the head, then diff
   the outputs. Every changed line must be one the PR intends. A line that should change but does not means
   the fix is incomplete.
2. **Fuzz.** Run `Hostile.EveryTruncation` and `Hostile.ByteFlips` in tests over every new or changed parse
   path, on both span and stream entry points. Run `tools/fuzz/fuzz.cs` over real files as well.
3. **Depth.** Run `NestingDepthTests`. If the change adds or touches a structure that nests, add a
   `DepthCase` first.
4. **Allocation bounds.** Use `Bounds.AssertAllocatesAtMost` or `Bounds.AssertLinearAllocation` on a large
   synthetic input for each new loop over file-controlled counts. Use `CountingStream` to pin how much a
   stream path reads.
5. **Reader comparison.** After every save path you changed, run `tools/compare/readers.sh` on the saved
   file. Also run `ffmpeg -v error -i out -f null -` where the file is decodable. Check that payload bytes are
   unchanged.
6. **Mutation testing.** Run `tools/mutation.sh`, which is Stryker.NET in diff mode, the equivalent of
   `dotnet stryker --since:origin/main` (see [tools/README.md](../tools/README.md#mutation-testing)).
   Every surviving mutant in your diff needs a test, or a sentence in the PR explaining why it is
   equivalent.

Then run the gates that CI runs:

- `dotnet build -c Release`, with 0 warnings on all four target frameworks.
- `dotnet test -c Release`, on net8.0 and net10.0.
- `dotnet format --verify-no-changes`.
- `dotnet pack -c Release`.
- `dotnet list package --vulnerable --include-transitive`.
- The env-gated integration tests, with their `TAGLIBSHARP2_TEST_*` variables set to real files.

## (e) Pre-review

Before you open the PR, have your diff reviewed independently at the checklist level a reviewer works at.
Use a colleague, or an agent given the diff and this guide but not your conclusions. The reviewer:

- checks each field the change reads or writes against the specification text, with citations;
- runs the change on real samples and compares the output with the black-box readers;
- runs the before/after grid on base and head, and over the whole corpus for detection;
- fuzzes the new parse paths and checks every length, count and offset against the bytes present;
- sweeps for twins using your twin table, and looks for rows that are missing from it;
- reverts a handful of your guards one at a time, where a revert that stays green is a finding;
- runs the gates, and checks every claim in the PR body against the head.

Fix what the review finds before you open the PR. A finding you decline needs evidence in the PR: a
specification quote or a command and its output.

## (f) Fix rounds

A fix is new code, so steps (b) to (e) apply to it in full.

- Write a twin table for the fix, covering every caller of the rule you changed and not just the line the
  finding cites.
- For each finding, name the pinning test. It must fail at the old head and pass at the new one. Run it both
  ways before you push.
- Re-run the kit on the new code. Many round-3+ findings were introduced by the round-2 fix.
- Re-run the previous round's grid at base, old head and new head. Every changed answer is either one a
  finding asked for or a regression.
- Update the PR body so it is true again.

## (g) Sequencing

- **Shared contracts land first.** A change to a contract other PRs build on goes in its own PR, ahead of
  them. Examples are a `Tag` member, a view's forwarding rule, a detection precedence or a shared parser.
- **PRs stay independent.** Do not fold one PR into another to get it reviewed. Stack only when the
  dependency is real, and say so in both PR bodies.
- **Re-run the kit whenever main moves.** Rebase, then run the gates, the grid and the depth tests again. A
  merge to main can turn a sibling's twin into your bug. One example was a new view type that did not
  forward a member your PR added.

## Kit reference

### Test helpers (`tests/TagLibSharp2.Tests/Kit/`)

| helper | use |
|--------|-----|
| `Bounds.AllocatedBytes (action)` | Bytes allocated on this thread while `action` runs. Synchronous code only. |
| `Bounds.AssertAllocatesAtMost (limit, action)` | Fails with the count when a parse allocates past a bound. |
| `Bounds.AssertLinearAllocation (build, run, n)` | Measures n, 2n and 4n, and fails when the second increment is more than 3× the first (linear work is 2×). This catches quadratic copies even behind a fixed cost. |
| `CountingStream` | Counts bytes read, reads and seeks, to pin that a stream path reads only what it needs. |
| `Hostile.EveryTruncation (valid, read)` | Runs every prefix through `read`, then reports how many threw and the first lengths. |
| `Hostile.ByteFlips (valid, read)` | Changes each byte to 0x00 and 0xFF and XORs it with 0x01 and 0x80, reporting the offset and value of each throw. `values:` sets the replacement values. |
| `Nesting.EbmlChain`, `IsoBoxChain`, `RiffListChain`, `Id3v2ChapterChain`, `Id3v2TocChain` | Builds a 100,000-level structure in one pass, using the widest size field each format allows. |
| `DepthCases` / `NestingDepthTests` | Every structure a file can nest, read and edited and saved in a child process on a 1 MiB stack with a 1 GiB heap. A stack overflow fails one test and does not abort the run. A case passes when nothing crashes or throws, and a save that succeeds reads back with the edit. A bounded refusal (a failure result) also passes. |

Add a nesting structure in three steps:

1. Add a `DepthCase` with a builder.
2. Check that `DepthCaseTests` reads a shallow copy of it.
3. Run `NestingDepthTests`.

If the case fails on main, add its name and the reason to `KnownOverflows`. That entry skips the row until
the fix lands. When the fix lands, delete the entry.

### Tools (`tools/`)

See [tools/README.md](../tools/README.md) for options and example output.

```bash
# Before/after: build the grid at the base and at the head, then diff.
git worktree add --detach ../base origin/main
dotnet run tools/compare/grid.cs -- --lib ../base > base.tsv   # files from TAGLIBSHARP2_TEST_CORPUS
dotnet run tools/compare/grid.cs --              > head.tsv
diff -a base.tsv head.tsv

# What other readers see after a save.
tools/compare/readers.sh saved.m4a

# Truncation and byte-flip fuzz over real files: span, stream and a no-op save.
dotnet run tools/fuzz/fuzz.cs -- --cuts 400 --flips 400 path/to/samples

# Mutation testing of the library files changed since main (Stryker.NET diff mode).
# Stryker's own --since sees no changes in a git worktree; the script takes them from git diff.
tools/mutation.sh origin/main
```

Mutation testing is local only. A full run of the library is too slow for CI, and the diff-mode run takes
tens of minutes even for a one-file diff (see [tools/README.md](../tools/README.md#mutation-testing)).
