# Tools

Local tools for the checks in [docs/IMPLEMENTATION-GUIDE.md](../docs/IMPLEMENTATION-GUIDE.md). None of them
runs in CI, and none of them belongs in the package.

| tool | what it does |
|------|--------------|
| [`compare/grid.cs`](compare/grid.cs) | Before/after grid: detection, key tags and a hash of each fixed edit's saved bytes, one TSV line per file |
| [`compare/readers.sh`](compare/readers.sh) | Shows what ffprobe, exiftool, mutagen and mediainfo read for common fields, side by side |
| [`fuzz/fuzz.cs`](fuzz/fuzz.cs) | Truncation and byte-flip fuzz over real files: span read, stream read and a no-op save |
| [`mutation.sh`](mutation.sh) | Stryker.NET mutation testing of the library files a branch changes |
| [`GenerateApiMatrix`](GenerateApiMatrix.cs) | Writes the API matrix from a built assembly |

The `.cs` tools are .NET file-based apps. They need SDK 10.0.300 or later, because they use `#:include`
to share `Shared/ToolSupport.cs`. They reference `src/TagLibSharp2` by a relative path, so they always
build against the library in the same checkout. Pass `--lib <worktree|csproj>` to build and run a copy
against another checkout's library instead. That is how you get the "before" side of a comparison from a
worktree of the base branch, even when that worktree has no `tools/` directory of its own. The copy is
built with `--no-cache`, so a library change is always picked up.

## Sample corpora

Never commit media. Point the tools at corpora on disk:

- Pass files or directories as arguments. Directories are searched recursively.
- With no arguments, the tools read `TAGLIBSHARP2_TEST_CORPUS`, a list of files and directories separated
  by `:` (or `;` on Windows). They also read every other `TAGLIBSHARP2_TEST_*` variable that names a file.
  These are the same variables the env-gated integration tests use, such as `TAGLIBSHARP2_TEST_MATROSKA`.

Only media extensions are read by default. `--all` reads every file except source and text files, which
are never opened. `--max-mb N` skips larger files: the default limit is 256 for the grid and 32 for the fuzz.

```bash
export TAGLIBSHARP2_TEST_CORPUS=~/samples/taglib-sharp:~/samples/TaglibSharpSamples
```

## compare/grid.cs

```bash
git worktree add --detach ../tls2-base origin/main
dotnet run tools/compare/grid.cs -- --lib ../tls2-base > base.tsv   # before
dotnet run tools/compare/grid.cs --                    > head.tsv   # after
diff -a base.tsv head.tsv
```

Each file is read with `MediaFile.Read (path)`. The grid then applies five edits, each to a fresh read:
`noop`, `title`, `artist`, `longtitle` (5,000 characters, which forces the tag to grow or move) and
`clear`. Each edit is saved through the file type's own `SaveToFile` to a temporary file, and its cell is
the first 12 hex digits of the SHA-256 of the saved bytes.

Markers after a hash:

| marker | meaning |
|--------|---------|
| `=same` | The saved file is byte-identical to the input. |
| `!reread` | The saved file does not read back. |
| `!value` | The saved file does not hold the edit. |

The `span` column is `=` when `MediaFile.ReadFromData` agrees with the path read on format and title. A
`!value` marker is a lead to follow up, not a verdict. For example, an ID3v1-only file cannot hold a
5,000-character title.

Example output from the TagLib# sample files, with columns cut for width:

```text
file              format  title      artist      album      year  track  duration_ms  span  noop               title         artist        longtitle           clear
Turning Lime.mpg  MpegPs  -          -           -          -     -      1130         =     no-tag             no-tag        no-tag        no-tag              no-tag
sample.m4a        Mp4     M4A title  M4A artist  M4A album  1234  6      5253         =     30745F14CA5A       2DA3AE441A55  98C8CE90E147  C659E838A85D        F67E933BC2E1
sample.wav        Wav     Title      Artist      Album      2009  1      2000         =     580E3F312678       6FCA1671EF91  4B170CE9B501  A66623BDE9B9        9A91B179E1A3!value
sample_v1_only.mp3 Mp3    MP3 title  MP3 artist  MP3 album  1234  6      1306         =     001209053E95=same  60FA44AE35BD  1F34858C9188  16DC1B9F572C!value  06F613BCAC49
a.mp3             FAIL Invalid MP3 file: data too short
```

Run with `--lib` pointed at a worktree of `4ef8e24~1`, the commit before MPEG program stream support,
and diffed against `main` at `577eaaf`, the grid changes on 10 of the 49 lines. Each change traces to a
commit in between:

- the two `.mpg` files, from MPEG-PS support;
- `sample.aac` and `corrupt/a.aac`, from the AAC format;
- `Turning Lime.mkv`, where the no-op save became byte-identical;
- the `null_title_v2.mp3` title;
- the save hashes of `sample.mp3`, `sample.wav`, `sample_replaygain.mp3` and `sample_v2_4_unsynch.mp3`.

```text
< Turning Lime.mpg	FAIL Unknown or unsupported file format: .../Turning Lime.mpg
> Turning Lime.mpg	MpegPs	-	-	-	-	-	1130	=	no-tag	no-tag	no-tag	no-tag	no-tag
< sample.aac	Mp3	AAC title	AAC artist	AAC album	1234	6	-	=	CA51FF50BF34	...
> sample.aac	Aac	AAC title	AAC artist	AAC album	1234	6	7337	=	CA51FF50BF34	...
< Turning Lime.mkv	Matroska	Turning Lime	...	BECEFFE62710	...
> Turning Lime.mkv	Matroska	Turning Lime	...	326890A266A0=same	...
```

## compare/readers.sh

```bash
tools/compare/readers.sh saved.m4a              # field table
tools/compare/readers.sh --raw saved.m4a        # every tag each tool reports
TAGLIBSHARP2_PYTHON=~/venvs/mutagen/bin/python tools/compare/readers.sh saved.mp3
```

exiftool runs with `-G1`, so each tag block's copy of a field is listed with its group, for example
`MP3 title v2 [id3v2_3] | MP3 title [id3v1]`, and a stale copy cannot hide. Each tool is optional. A tool
that is not installed shows `n/a`, and a field the tool does not report
shows `-`. mutagen runs under `$TAGLIBSHARP2_PYTHON` (default `python3`). Python runs isolated (`-I`), which ignores
the user site, so point that variable at a virtual environment that has mutagen installed. python3 itself is required. These tools only run as black boxes. TagLib# and
AVFoundation are not included: run them yourself as external readers, and never add TagLib# as a
dependency anywhere in this repository.

```text
# samples/sample_both.mp3   (an ID3v2.3 tag and an ID3v1 tag that disagree)
field        ffprobe       exiftool                                                        mutagen         mediainfo
title        MP3 title v2  MP3 title v2 [id3v2_3] | MP3 title [id3v1]                      MP3 title v2    MP3 title v2
artist       -             MP3 artist [id3v1]                                              MP3 artist      MP3 artist
album        MP3 album v2  MP3 album v2 [id3v2_3] | MP3 album [id3v1]                      MP3 album v2    MP3 album v2
albumartist  -             -                                                               -               -
date         1234          1234 [id3v2_3] | 1235 [id3v1]                                   1234            1234
track        06/07         06/07 [id3v2_3] | 6 [id3v1]                                     06/07           06
disc         5             5 [id3v2_3]                                                     5               5
genre        Acid Punk     Acid Punk [id3v2_3] | Acid Punk [id3v1]                         Acid Punk       Acid Punk
comment      -             (ID3v1 Comment) MP3 comment v2 [id3v2_3] | MP3 comment [id3v1]  MP3 comment v2  MP3 comment
composer     MP3 composer  MP3 composer [id3v2_3]                                          MP3 composer    MP3 composer
```

## fuzz/fuzz.cs

```bash
dotnet run tools/fuzz/fuzz.cs -- [--cuts 400] [--flips 400] [--seed 1] [--slow-ms 2000] [--no-save] <file|dir>...
```

Each input goes through `MediaFile.ReadFromData` (the span path), then `MediaFile.Read (Stream)` (the
stream path). When the stream read succeeds, a no-op save to an in-memory file system follows.

- **Cuts:** every length within 256 bytes of either end, plus `--cuts` evenly spaced lengths.
- **Flips:** `--flips` random single-byte changes (0x00, 0xFF, XOR 0x01, XOR 0x80). Half of them land in the
  first or last 64 KiB, where the headers and tags are.

Every exception is printed with the cut or the offset, the step that threw and the first library frame.
Every case slower than `--slow-ms` is printed too. The exit code is 1 when anything threw. A stack overflow
ends the process: use `NestingDepthTests` for depth.

On main, a run over the TagLib# samples finds that a WAV file cut to between 12 and 35 bytes reads, but
`SaveToFile` then throws:

```text
sample.wav	cut 12	save	InvalidOperationException: Cannot render invalid WAV file	at TagLibSharp2.Riff.WavFile.Render() in .../WavFile.cs:line 462
...
sample.mp3	bytes=11815	cases=1297	throws=0	slow=0
sample.wav	bytes=186992	cases=1313	throws=24	slow=0
total	files=49	cases=47059	throws=24	slow=0
```

## Depth tests (in the test project)

`NestingDepthTests` runs every `DepthCase` 100,000 levels deep in a child process. The child is
`dotnet exec TagLibSharp2.Tests.dll kit-depth <case> <depth>`, running on a 1 MiB stack with a 1 GiB GC
heap limit. You can run one case by hand:

```bash
dotnet build -c Release tests/TagLibSharp2.Tests
dotnet exec tests/TagLibSharp2.Tests/bin/Release/net10.0/TagLibSharp2.Tests.dll kit-depth wav-list 100000
```

```text
wav-list depth=100000 bytes=1200058 span=ok(Wav) stream=ok(Wav) async=ok(Wav) path=ok(Wav) edit=ok save=ok reread=ok(Wav)
```

A case passes when nothing crashes or throws and a save that succeeds reads back with the edit. A bounded
refusal (a failure result) also passes. A case that crashes exits non-zero, and `Stack overflow.` and the
repeating frame appear on stderr. On main, `kit-depth mp4-udta 5000` shows the MP4 box parser recursing:

```text
Stack overflow.
1612    at TagLibSharp2.Mp4.Mp4BoxParser.ParseBox(System.ReadOnlySpan`1<Byte>, Int32, Int64)
1611    at TagLibSharp2.Mp4.Mp4BoxParser.ParseChildren(System.ReadOnlySpan`1<Byte>, Int32 ByRef)
```

These are the frame counts from `sort | uniq -c` on stderr.

## Mutation testing

```bash
tools/mutation.sh                 # library files changed since origin/main
tools/mutation.sh origin/main~3   # any revision
```

`.config/dotnet-tools.json` pins `dotnet-stryker`, and its settings are in
`tests/TagLibSharp2.Tests/stryker-config.json` (net10.0, Release, per-test coverage). The script takes the
changed files from `git diff` against the merge base and passes them to Stryker as `--mutate` patterns.
Reports go to `tests/TagLibSharp2.Tests/StrykerOutput/`, which git ignores.

The script works around two limits of Stryker 5.1:

- Stryker's own `--since:<branch>` finds no changes in a git worktree, and it accepts only plain branch
  names.
- Its VSTest runner cannot start the test host under the .NET 11 SDK. When the default SDK is 11, the script
  selects an installed 10.0 SDK through a temporary `global.json`, and removes the file afterwards.

Mutation testing is local only. Stryker creates about 23,000 mutants for the whole library, and the setup
before any mutant runs (build, the first test run and coverage capture) already takes 3 to 12 minutes. See
the timings below.

Measured on an Apple-silicon Mac shared with other heavy jobs. The run was main's last MPEG-PS fix
(`tools/mutation.sh origin/main~2`, which changes one file, `MpegPsScanner.cs`), with SDK 10.0.403 and
Stryker 5.1.0:

| step | result |
|------|--------|
| mutants created for the whole library | 23,258 |
| mutants tested (one changed file) | 578 |
| result | 406 killed, 153 survived, 19 timeouts, 12 not covered: score 72.03% |
| wall time | 22.4 minutes in total, of which Stryker reports 9 minutes 38 seconds after its setup. The build, the first run of the 4,831 tests Stryker found at that commit (`ebd6138`) and coverage capture took between 3 and 12 minutes, depending on machine load. |

Testing all 23,000 mutants would take hours, and even a one-file diff takes tens of minutes, so mutation
testing is not part of CI. Run it before you open a PR and before each fix round, then read the
survivors in `StrykerOutput/<run>/reports/mutation-report.html`.

Stryker's per-test coverage sees only code that runs in the test host. The depth tests run their cases in
a child process, so a mutant that only a depth test would kill is reported as not covered or as
surviving.
