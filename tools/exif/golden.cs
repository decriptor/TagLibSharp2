#!/usr/bin/env dotnet
#:property PublishAot=false
#:property NoWarn=$(NoWarn);CA1308;CA1031;CA1303;CA1305;CA1307;CA1310;CA1515;CA1812;CA1849;CA2007;CA5394;CS1591
// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// Records what exiftool shows for every Exif input the tests generate. Usage: golden.cs <dump-dir> <golden.txt>
// <dump-dir> holds NNNNN.tif files (raw TIFF) written by the tests' dump mode (see golden.sh). Each is wrapped in a
// JPEG APP1 segment, exiftool reads them all, and the golden file gets one summary per input, in order: the field tags
// shown in tables read with Exif tags ("E" and the tag in hex) and GPSLatitudeRef in GPS tables ("G0001"), counted.
// The summaries are stored once each and the inputs refer to them by number, with the inputs' SHA-256 to catch drift.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var dump = args[0];
var output = args[1];
var tiffs = Directory.GetFiles (dump, "*.tif").OrderBy (f => f, StringComparer.Ordinal).ToArray ();
var jpegs = Path.Combine (Path.GetTempPath (), "exif-golden-" + Guid.NewGuid ().ToString ("N"));
Directory.CreateDirectory (jpegs);
using var sha = IncrementalHash.CreateHash (HashAlgorithmName.SHA256);
foreach (var path in tiffs) {
	var tiff = File.ReadAllBytes (path);
	sha.AppendData (tiff);
	var app1 = new byte[6 + tiff.Length];
	"Exif\0\0"u8.CopyTo (app1);
	tiff.CopyTo (app1, 6);
	using var jpeg = new MemoryStream ();
	jpeg.Write ([0xFF, 0xD8, 0xFF, 0xE1, (byte)((app1.Length + 2) >> 8), (byte)(app1.Length + 2)]);
	jpeg.Write (app1);
	jpeg.Write ([0xFF, 0xD9]);
	File.WriteAllBytes (Path.Combine (jpegs, Path.GetFileNameWithoutExtension (path) + ".jpg"), jpeg.ToArray ());
}

int[] fieldTags = [0x9286, 0x9C9B, 0x9C9C, 0x9C9D, 0x010E, 0x013B, 0x8298];
var counts = new Dictionary<string, SortedDictionary<string, int>> ();
var start = new ProcessStartInfo ("exiftool", ["-a", "-G1", "-H", "-s", "-U", "-EXIF:All", jpegs]) { RedirectStandardOutput = true, RedirectStandardError = true };
using (var exiftool = Process.Start (start)!) {
	_ = exiftool.StandardError.ReadToEndAsync ();
	string? current = null;
	while (exiftool.StandardOutput.ReadLine () is string line) {
		var file = Regex.Match (line, @"^======== .*[/\\](\d+)\.jpg$");
		if (file.Success) {
			current = file.Groups[1].Value;
			counts[current] = new (StringComparer.Ordinal);
			continue;
		}

		var tag = Regex.Match (line, @"^\[(\w+)\]\s+0x([0-9a-f]{4}) ");
		if (current is null || !tag.Success)
			continue;
		var group = tag.Groups[1].Value;
		var id = Convert.ToInt32 (tag.Groups[2].Value, 16);
		var key = group.StartsWith ("GPS", StringComparison.Ordinal) ? (id == 0x0001 ? "G0001" : null)
			: Array.IndexOf (fieldTags, id) >= 0 ? "E" + id.ToString ("X4") : null;
		if (key is not null)
			counts[current][key] = counts[current].GetValueOrDefault (key) + 1;
	}

	exiftool.WaitForExit ();
}

var version = Process.Start (new ProcessStartInfo ("exiftool", ["-ver"]) { RedirectStandardOutput = true })!.StandardOutput.ReadToEnd ().Trim ();
var summaries = new List<string> ();
var sequence = new List<int> ();
foreach (var path in tiffs) {
	var name = Path.GetFileNameWithoutExtension (path);
	var summary = counts.TryGetValue (name, out var c) && c.Count > 0 ? string.Join (" ", c.Select (p => $"{p.Key}={p.Value}")) : "-";
	var index = summaries.IndexOf (summary);
	if (index < 0) {
		index = summaries.Count;
		summaries.Add (summary);
	}

	sequence.Add (index);
}

var text = new StringBuilder ();
text.Append ($"# What exiftool {version} shows for each generated input; regenerate with tools/exif/golden.sh.\n");
text.Append ($"# inputs {tiffs.Length} sha256 {Convert.ToHexString (sha.GetHashAndReset ()).ToLowerInvariant ()}\n");
text.Append ("@summaries\n");
for (var n = 0; n < summaries.Count; n++)
	text.Append ($"{n} {summaries[n]}\n");
text.Append ("@inputs\n");
for (var n = 0; n < sequence.Count; n += 40)
	text.Append (string.Join (" ", sequence.Skip (n).Take (40))).Append ('\n');
File.WriteAllText (output, text.ToString ());
Directory.Delete (jpegs, recursive: true);
Console.WriteLine ($"{tiffs.Length} inputs, {summaries.Count} distinct summaries -> {output}");
