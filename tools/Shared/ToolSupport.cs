// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// Shared by the file-based tools in tools/compare and tools/fuzz (included with #:include).
// Uses only public library API that has been stable for many releases, so a copy built
// against an older base worktree still compiles.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TagLibSharp2.Core;

static class ToolSupport
{
	/// <summary>Environment variable: path-separated files or directories to use when no paths are given.</summary>
	public const string CorpusVariable = "TAGLIBSHARP2_TEST_CORPUS";

	static readonly HashSet<string> MediaExtensions = new (StringComparer.OrdinalIgnoreCase) {
		".mp3", ".m4a", ".m4b", ".m4p", ".m4v", ".mp4", ".mov", ".3gp", ".mkv", ".mka", ".mks", ".mk3d", ".webm",
		".flac", ".ogg", ".oga", ".ogv", ".opus", ".spx", ".wav", ".aif", ".aiff", ".aifc", ".dsf", ".dff",
		".wv", ".ape", ".mpc", ".mp+", ".mpp", ".wma", ".wmv", ".asf", ".mpg", ".mpeg", ".mpe", ".vob",
		".mpv", ".m1v", ".m2v", ".avi", ".aac", ".aa", ".aax", ".tta",
	};

	// Never opened, even with --all: source and text that sits beside sample media.
	static readonly HashSet<string> TextExtensions = new (StringComparer.OrdinalIgnoreCase) {
		".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".md", ".txt", ".json", ".xml", ".yml", ".yaml",
		".py", ".sh", ".config", ".c", ".cpp", ".h", ".java", ".js", ".ts", ".html", ".gitignore", ".gitattributes",
	};

	/// <summary>
	/// When the arguments name <c>--lib &lt;worktree|csproj&gt;</c>, runs a copy of this tool built against
	/// that library and returns its exit code. Otherwise returns null and the tool runs here.
	/// </summary>
	public static int? RunAgainstLibrary (string[] args)
	{
		var at = Array.IndexOf (args, "--lib");
		if (at < 0)
			return null;
		if (at + 1 >= args.Length)
			throw new ArgumentException ("--lib needs a worktree directory or a TagLibSharp2.csproj path");

		var project = ResolveProject (args[at + 1]);
		var rest = args.Where ((_, i) => i != at && i != at + 1).ToArray ();
		var entry = AppContext.GetData ("EntryPointFilePath") as string
			?? throw new InvalidOperationException ("--lib needs SDK 10 file-based apps (EntryPointFilePath)");
		var toolsRoot = Path.GetDirectoryName (Path.GetDirectoryName (entry))!;
		var copyRoot = Path.Combine (Path.GetTempPath (), "tls2-tools", ShortHash (System.Text.Encoding.UTF8.GetBytes (project)));

		// Copy the tool sources with the same layout so #:include paths still resolve,
		// and point the entry file's #:project at the chosen library.
		foreach (var source in Directory.EnumerateFiles (toolsRoot, "*.cs", SearchOption.AllDirectories)) {
			var relative = Path.GetRelativePath (toolsRoot, source);
			if (relative.Split (Path.DirectorySeparatorChar)[0] is not ("compare" or "fuzz" or "Shared"))
				continue;
			var target = Path.Combine (copyRoot, relative);
			Directory.CreateDirectory (Path.GetDirectoryName (target)!);
			var lines = File.ReadAllLines (source);
			if (Path.GetFullPath (source) == Path.GetFullPath (entry))
				lines = [.. lines.Select (l => l.StartsWith ("#:project ", StringComparison.Ordinal) ? "#:project " + project : l)];
			File.WriteAllLines (target, lines);
		}

		var copy = Path.Combine (copyRoot, Path.GetRelativePath (toolsRoot, entry));
		Console.Error.WriteLine ($"# library: {project}");
		var start = new ProcessStartInfo (DotnetHost ()) { UseShellExecute = false };
		// --no-cache: the copy's sources do not change when the library does, so force a build.
		foreach (var argument in new[] { "run", "--no-cache", "--file", copy, "--" }.Concat (rest))
			start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		process.WaitForExit ();
		return process.ExitCode;
	}

	static string ResolveProject (string path)
	{
		var full = Path.GetFullPath (path);
		if (File.Exists (full) && full.EndsWith (".csproj", StringComparison.OrdinalIgnoreCase))
			return full;
		var inWorktree = Path.Combine (full, "src", "TagLibSharp2", "TagLibSharp2.csproj");
		if (File.Exists (inWorktree))
			return inWorktree;
		throw new ArgumentException ($"--lib {path}: no TagLibSharp2.csproj there or under src/TagLibSharp2");
	}

	static string DotnetHost ()
	{
		var fromSdk = Environment.GetEnvironmentVariable ("DOTNET_HOST_PATH");
		if (!string.IsNullOrEmpty (fromSdk) && File.Exists (fromSdk))
			return fromSdk;
		var name = RuntimeInformation.IsOSPlatform (OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
		var beside = Path.Combine (Path.GetFullPath (Path.Combine (RuntimeEnvironment.GetRuntimeDirectory (), "..", "..", "..")), name);
		return File.Exists (beside) ? beside : name;
	}

	/// <summary>
	/// The media files under <paramref name="paths"/> (files as given, directories recursively), sorted.
	/// With no paths: <see cref="CorpusVariable"/>, then every <c>TAGLIBSHARP2_TEST_*</c> variable naming a file.
	/// </summary>
	public static List<string> MediaFiles (IEnumerable<string> paths, bool all, long maxBytes)
	{
		var roots = paths.ToList ();
		if (roots.Count == 0) {
			var corpus = Environment.GetEnvironmentVariable (CorpusVariable);
			if (!string.IsNullOrEmpty (corpus))
				roots.AddRange (corpus.Split (Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
			foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables ()) {
				if (variable.Key is string key && key.StartsWith ("TAGLIBSHARP2_TEST_", StringComparison.Ordinal)
					&& key != CorpusVariable && variable.Value is string value && File.Exists (value))
					roots.Add (value);
			}
		}

		var files = new SortedSet<string> (StringComparer.Ordinal);
		foreach (var root in roots) {
			if (File.Exists (root)) {
				files.Add (Path.GetFullPath (root));
				continue;
			}

			if (!Directory.Exists (root)) {
				Console.Error.WriteLine ($"# skipped (not found): {root}");
				continue;
			}

			foreach (var file in Directory.EnumerateFiles (root, "*", SearchOption.AllDirectories)) {
				var name = Path.GetFileName (file);
				var extension = Path.GetExtension (file);
				if (name.StartsWith ('.') || TextExtensions.Contains (extension) || name is "LICENSE" or "README")
					continue;
				if (!all && !MediaExtensions.Contains (extension))
					continue;
				if (new FileInfo (file).Length > maxBytes) {
					Console.Error.WriteLine ($"# skipped (over --max-mb): {file}");
					continue;
				}

				files.Add (Path.GetFullPath (file));
			}
		}

		return [.. files];
	}

	delegate FileWriteResult SaveWithData (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem);

	/// <summary>
	/// Saves through the file's own type: <c>SaveToFile (path, originalData, fs)</c> where it exists,
	/// else <c>SaveToFile (path, fs)</c> (WAV and AIFF keep their bytes).
	/// </summary>
	public static FileWriteResult Save (object file, ReadOnlySpan<byte> originalData, string path, IFileSystem? fileSystem)
	{
		var type = file.GetType ();
		var withData = type.GetMethod ("SaveToFile", [typeof (string), typeof (ReadOnlySpan<byte>), typeof (IFileSystem)]);
		if (withData is not null)
			return withData.CreateDelegate<SaveWithData> (file) (path, originalData, fileSystem);
		var withPath = type.GetMethod ("SaveToFile", [typeof (string), typeof (IFileSystem)])
			?? throw new InvalidOperationException ($"{type.Name} has no SaveToFile overload");
		try {
			return (FileWriteResult)withPath.Invoke (file, [path, fileSystem])!;
		} catch (TargetInvocationException ex) when (ex.InnerException is not null) {
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture (ex.InnerException).Throw ();
			throw;
		}
	}

	/// <summary>The first 12 hex digits of the SHA-256 of <paramref name="data"/>.</summary>
	public static string ShortHash (ReadOnlySpan<byte> data) => Convert.ToHexString (SHA256.HashData (data))[..12];

	/// <summary>Parses <c>--name value</c> as an integer, or returns the default.</summary>
	public static long Option (List<string> args, string name, long fallback)
	{
		var at = args.IndexOf (name);
		if (at < 0)
			return fallback;
		if (at + 1 >= args.Count || !long.TryParse (args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
			throw new ArgumentException ($"{name} needs a number");
		args.RemoveRange (at, 2);
		return value;
	}

	/// <summary>Removes <paramref name="name"/> from the arguments and reports whether it was there.</summary>
	public static bool Flag (List<string> args, string name) => args.Remove (name);

	/// <summary>A value as one TSV cell: control characters (tabs, newlines, NULs) become spaces.</summary>
	public static string Cell (string? value) =>
		value is null ? "-" : string.Concat (value.Select (c => char.IsControl (c) ? ' ' : c));
}

/// <summary>An in-memory <see cref="IFileSystem"/> so a save costs no disk I/O.</summary>
sealed class MemoryFiles : IFileSystem
{
	readonly Dictionary<string, byte[]> _files = new (StringComparer.Ordinal);

	public bool FileExists (string path) => _files.ContainsKey (path);
	public Stream OpenRead (string path) => new MemoryStream (Get (path), writable: false);
	public Stream OpenReadWrite (string path) => new Stored (this, path, Get (path));
	public Stream Create (string path) => new Stored (this, path, []);
	public byte[] ReadAllBytes (string path) => Get (path);
	public Task<byte[]> ReadAllBytesAsync (string path, CancellationToken cancellationToken = default) => Task.FromResult (Get (path));
	public void WriteAllBytes (string path, ReadOnlySpan<byte> data) => _files[path] = data.ToArray ();

	public Task WriteAllBytesAsync (string path, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		_files[path] = data.ToArray ();
		return Task.CompletedTask;
	}

	public void Delete (string path) => _files.Remove (path);

	public void Move (string sourcePath, string destinationPath)
	{
		_files[destinationPath] = Get (sourcePath);
		_files.Remove (sourcePath);
	}

	public string? GetDirectoryName (string path) => Path.GetDirectoryName (path);
	public string GetFileName (string path) => Path.GetFileName (path);
	public string CombinePath (string path1, string path2) => Path.Combine (path1, path2);

	byte[] Get (string path) => _files.TryGetValue (path, out var data) ? data : throw new FileNotFoundException (path);

	// A writable copy whose bytes land in the store when it is closed.
	sealed class Stored : MemoryStream
	{
		readonly MemoryFiles _owner;
		readonly string _path;

		public Stored (MemoryFiles owner, string path, byte[] initial)
		{
			_owner = owner;
			_path = path;
			Write (initial, 0, initial.Length);
			Position = 0;
		}

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				_owner._files[_path] = ToArray ();
			base.Dispose (disposing);
		}
	}
}
