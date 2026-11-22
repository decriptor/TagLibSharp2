#!/usr/bin/env dotnet
#:project ../../src/TagLibSharp2/TagLibSharp2.csproj
#:include ../Shared/ToolSupport.cs
#:property PublishAot=false
#:property AnalysisLevel=latest-recommended
#:property NoWarn=$(NoWarn);CA1031;CA1303;CA1305;CA1307;CA1310;CA1515;CA1812;CA1849;CA2007;CS1591
// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// Before/after grid: one TSV line per file with what MediaFile detects and reads, and a hash of
// the bytes each fixed edit saves. Run it against the base and the head and diff the two outputs.
//
//   dotnet run tools/compare/grid.cs -- [--lib <worktree|csproj>] [--all] [--max-mb N] <file|dir>...
//
// See tools/README.md.

using System.Diagnostics;
using TagLibSharp2.Core;

var fromLibrary = ToolSupport.RunAgainstLibrary (args);
if (fromLibrary is int code)
	return code;

var options = args.ToList ();
var all = ToolSupport.Flag (options, "--all");
var maxBytes = ToolSupport.Option (options, "--max-mb", 256) * 1024 * 1024;
var files = ToolSupport.MediaFiles (options, all, maxBytes);
if (files.Count == 0) {
	Console.Error.WriteLine ($"no media files: pass files or directories, or set {ToolSupport.CorpusVariable}");
	return 2;
}

const string NewTitle = "Grid Title";
const string NewArtist = "Grid Artist";
var longTitle = new string ('L', 5000);
var edits = new (string Name, Action<Tag> Apply, Func<Tag?, bool> Check)[] {
	("noop", _ => { }, _ => true),
	("title", t => t.Title = NewTitle, t => t?.Title == NewTitle),
	("artist", t => t.Artist = NewArtist, t => t?.Artist == NewArtist),
	("longtitle", t => t.Title = longTitle, t => t?.Title == longTitle),
	("clear", t => t.Clear (), t => t is null || t.IsEmpty),
};

string[] columns = ["file", "format", "title", "artist", "album", "year", "track", "duration_ms", "span"];
Console.WriteLine (string.Join ('\t', columns.Concat (edits.Select (e => e.Name))));
var scratch = Path.Combine (Path.GetTempPath (), "tls2-grid-" + Environment.ProcessId);
Directory.CreateDirectory (scratch);
try {
	foreach (var path in files)
		Console.WriteLine (Row (path));
} finally {
	Directory.Delete (scratch, recursive: true);
}

return 0;

string Row (string path)
{
	var name = Path.GetFileName (path);
	MediaFileResult read;
	try {
		read = MediaFile.Read (path);
	} catch (Exception ex) {
		return $"{ToolSupport.Cell (name)}\tTHREW {ex.GetType ().Name}: {ToolSupport.Cell (ex.Message)}";
	}

	if (!read.IsSuccess)
		return $"{ToolSupport.Cell (name)}\tFAIL {ToolSupport.Cell (read.Error)}";

	var tag = read.Tag;
	var duration = (read.File as IMediaFile)?.AudioProperties?.Duration.TotalMilliseconds;
	(read.File as IDisposable)?.Dispose ();
	var cells = new List<string> {
		ToolSupport.Cell (name), read.Format.ToString (), ToolSupport.Cell (tag?.Title), ToolSupport.Cell (tag?.Artist),
		ToolSupport.Cell (tag?.Album), ToolSupport.Cell (tag?.Year), tag?.Track?.ToString (System.Globalization.CultureInfo.InvariantCulture) ?? "-",
		duration is double ms ? ((long)ms).ToString (System.Globalization.CultureInfo.InvariantCulture) : "-",
	};

	var original = File.ReadAllBytes (path);
	cells.Add (SpanAgrees (original, path, read, tag));
	foreach (var (editName, apply, check) in edits)
		cells.Add (Edit (path, original, apply, check));
	return string.Join ('\t', cells);
}

// The span entry point must agree with the path entry point on format and title.
static string SpanAgrees (byte[] original, string path, MediaFileResult read, Tag? tag)
{
	try {
		var span = MediaFile.ReadFromData (original, path);
		(span.File as IDisposable)?.Dispose ();
		if (span.IsSuccess && span.Format == read.Format && span.Tag?.Title == tag?.Title)
			return "=";
		return span.IsSuccess ? $"{span.Format}/{ToolSupport.Cell (span.Tag?.Title)}" : "FAIL";
	} catch (Exception ex) {
		return "THREW " + ex.GetType ().Name;
	}
}

// Hash of the saved bytes; "=same" when a no-op save is byte-identical; "!reread" or "!value"
// when the saved file does not read back or does not hold the edit.
string Edit (string path, byte[] original, Action<Tag> apply, Func<Tag?, bool> check)
{
	var output = Path.Combine (scratch, "out" + Path.GetExtension (path));
	try {
		var read = MediaFile.Read (path);
		if (!read.IsSuccess || read.File is null)
			return "FAIL";
		if (read.Tag is null) {
			(read.File as IDisposable)?.Dispose ();
			return "no-tag";
		}

		apply (read.Tag);
		var write = ToolSupport.Save (read.File, original, output, null);
		(read.File as IDisposable)?.Dispose ();
		if (!write.IsSuccess)
			return "SAVEFAIL " + ToolSupport.Cell (write.Error);

		var saved = File.ReadAllBytes (output);
		var cell = ToolSupport.ShortHash (saved);
		if (saved.AsSpan ().SequenceEqual (original))
			cell += "=same";
		var again = MediaFile.ReadFromData (saved, path);
		if (!again.IsSuccess)
			cell += "!reread";
		else if (!check (again.Tag))
			cell += "!value";
		(again.File as IDisposable)?.Dispose ();
		return cell;
	} catch (Exception ex) {
		return $"THREW {ex.GetType ().Name}: {ToolSupport.Cell (ex.Message)}";
	} finally {
		File.Delete (output);
	}
}
