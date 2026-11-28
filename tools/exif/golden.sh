#!/usr/bin/env bash
# Copyright (c) 2025-2026 Stephen Shaw and contributors
# Licensed under the MIT License. See LICENSE file in the project root for full license information.
#
# Regenerates the exiftool golden files the Exif reader tests check against:
#   tests/TagLibSharp2.Tests/Image/Golden/exif-reader-probes.txt   (the probes of each reading rule)
#   tests/TagLibSharp2.Tests/Image/Golden/exif-edit-matrix.txt     (every input of the Exif edit matrix)
# The tests write their inputs when TAGLIBSHARP2_EXIF_GOLDEN_DUMP names a directory; golden.cs runs exiftool over them.
# Needs exiftool on PATH. Text only: no media is written to the repository.
set -euo pipefail
root=$(git rev-parse --show-toplevel)
dump=$(mktemp -d)
trap 'rm -rf "$dump"' EXIT
TAGLIBSHARP2_EXIF_GOLDEN_DUMP="$dump" dotnet test "$root/tests/TagLibSharp2.Tests" -c Release -f net10.0 \
	--filter "FullyQualifiedName~ExifReaderGoldenTests" > /dev/null || true
for set in probes matrix; do
	name=$([[ $set == probes ]] && echo exif-reader-probes || echo exif-edit-matrix)
	dotnet "$root/tools/exif/golden.cs" "$dump/$set" "$root/tests/TagLibSharp2.Tests/Image/Golden/$name.txt"
done
