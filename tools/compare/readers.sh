#!/usr/bin/env bash
# Copyright (c) 2025-2026 Stephen Shaw and contributors
# Licensed under the MIT License. See LICENSE file in the project root for full license information.
#
# What other readers see in a file: one TSV row per common field, one column per tool.
# Run it after a save to confirm every reader shows the new value and no stale copy wins.
#
#   tools/compare/readers.sh [--raw] <file>...
#
# Tools (each optional, skipped with a note when missing): ffprobe, exiftool, mutagen, mediainfo.
# mutagen runs under $TAGLIBSHARP2_PYTHON (default python3), so point it at a venv that has mutagen.
# --raw prints every tag each tool reports as tool<TAB>key<TAB>value instead of the field table.
# python3 is required for the normalisation. Python runs isolated (-I), so mutagen must be importable
# without the user site: use a venv. These tools are only run as black boxes.

set -euo pipefail

raw=0
if [[ "${1:-}" == "--raw" ]]; then
	raw=1
	shift
fi
if [[ $# -eq 0 ]]; then
	echo "usage: $0 [--raw] <file>..." >&2
	exit 2
fi

python=${TAGLIBSHARP2_PYTHON:-python3}
work=$(mktemp -d "${TMPDIR:-/tmp}/tls2-readers.XXXXXX")
trap 'rm -rf "$work"' EXIT

have () { command -v "$1" > /dev/null 2>&1; }

for file in "$@"; do
	if [[ ! -f "$file" ]]; then
		echo "# not a file: $file" >&2
		continue
	fi
	rm -f "$work"/*.json

	if have ffprobe; then
		ffprobe -v error -show_entries format_tags:stream_tags -of json "$file" > "$work/ffprobe.json" 2> /dev/null || true
	fi
	if have exiftool; then
		exiftool -j -a -G1 -api largefilesupport=1 "$file" > "$work/exiftool.json" 2> /dev/null || true
	fi
	if have mediainfo; then
		mediainfo --Output=JSON "$file" > "$work/mediainfo.json" 2> /dev/null || true
	fi
	if "$python" -I -c 'import mutagen' > /dev/null 2>&1; then
		"$python" -I - "$file" > "$work/mutagen.json" 2> /dev/null <<'PY' || true
import json, sys
import mutagen
f = mutagen.File(sys.argv[1])
out = {}
if f is not None and f.tags is not None:
    for key, value in f.tags.items():
        values = value if isinstance(value, list) else [value]
        texts = []
        for v in values:
            text = getattr(v, "text", None)
            if text is not None:
                texts.append("; ".join(str(t) for t in text))
            elif isinstance(v, tuple):
                texts.append("/".join(str(p) for p in v))
            elif isinstance(v, (bytes, bytearray)):
                texts.append("<%d bytes>" % len(v))
            else:
                texts.append(str(getattr(v, "value", v)))
        out[str(key)] = "; ".join(texts)
print(json.dumps(out))
PY
	fi

	"$python" -I - "$work" "$file" "$raw" <<'PY'
import json, os, sys

work, path, raw = sys.argv[1], sys.argv[2], sys.argv[3] == "1"
TOOLS = ["ffprobe", "exiftool", "mutagen", "mediainfo"]

def load(tool):
    p = os.path.join(work, tool + ".json")
    if not os.path.exists(p) or os.path.getsize(p) == 0:
        return None
    try:
        data = json.load(open(p, encoding="utf-8", errors="replace"))
    except ValueError:
        return None
    tags = {}
    if tool == "ffprobe":
        for section in [data.get("format", {})] + data.get("streams", []):
            for k, v in (section.get("tags") or {}).items():
                tags.setdefault(k.lower(), str(v))
    elif tool == "exiftool":
        # -G1 keeps each tag block apart (ID3v2_4:Title, ID3v1:Title), so a stale copy shows up.
        for k, v in (data[0] if data else {}).items():
            group = k.split(":", 1)[0] if ":" in k else ""
            if k == "SourceFile" or group in ("ExifTool", "System", "File"):
                continue
            tags[k.lower()] = str(v)
    elif tool == "mediainfo":
        for track in (data.get("media") or {}).get("track", []):
            if track.get("@type") == "General":
                for k, v in track.items():
                    if isinstance(v, str):
                        tags[k.lower()] = v
                for k, v in (track.get("extra") or {}).items():
                    tags.setdefault(k.lower(), str(v))
    else:
        tags = {k.lower(): v for k, v in data.items()}
    return tags

# Normalised field -> keys each tool may use, lower case, first match wins.
FIELDS = {
    "title": ["title", "tit2", "©nam", "track_name"],
    "artist": ["artist", "tpe1", "©art", "author", "performer"],
    "album": ["album", "talb", "©alb", "wm/albumtitle"],
    "albumartist": ["album_artist", "albumartist", "tpe2", "aart", "band", "album_performer", "wm/albumartist"],
    "date": ["date", "year", "tdrc", "tyer", "©day", "recorded_date", "wm/year", "recordingtime", "contentcreatedate"],
    "track": ["track", "tracknumber", "trck", "trkn", "track_position", "wm/tracknumber"],
    "disc": ["disc", "discnumber", "disknumber", "tpos", "disk", "part_position", "partofset"],
    "genre": ["genre", "tcon", "©gen", "wm/genre"],
    "comment": ["comment", "©cmt", "description"],
    "composer": ["composer", "tcom", "©wrt", "wm/composer"],
}

# mediainfo names the track title "Track" and the track number "Track_Position".
OVERRIDES = {"mediainfo": {"title": ["title", "track"], "track": ["track_position"]}}

loaded = {t: load(t) for t in TOOLS}
print("# " + path)
if raw:
    for tool, tags in loaded.items():
        if tags is None:
            print(f"{tool}\t-\t(not installed or no output)")
            continue
        for k in sorted(tags):
            print(f"{tool}\t{k}\t{tags[k]}".replace("\n", " "))
    sys.exit(0)

def pick(tool, tags, keys):
    if tags is None:
        return "n/a"
    if tool == "exiftool":
        # Every tag block's value, so two disagreeing copies are both visible.
        found = []
        for key in keys:
            for k, v in tags.items():
                name = k.split(":", 1)[-1]
                if name == key or name.startswith(key + "-") or (key == "comment" and name.startswith("comment")):
                    group = k.split(":", 1)[0] if ":" in k else ""
                    item = f"{v} [{group}]" if group else v
                    if item not in found:
                        found.append(item)
            if found:
                return " | ".join(found)
        return "-"
    for key in keys:
        if key in tags:
            return tags[key]
        for k, v in tags.items():
            if k.startswith(key + ":") or (key == "comment" and k.startswith("comm")):
                return v
    return "-"

print("field\t" + "\t".join(TOOLS))
for field, keys in FIELDS.items():
    cells = [pick(t, loaded[t], OVERRIDES.get(t, {}).get(field, keys)).replace("\t", " ").replace("\n", " ")[:120] for t in TOOLS]
    print(field + "\t" + "\t".join(cells))
PY
done
