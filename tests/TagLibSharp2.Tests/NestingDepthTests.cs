// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Tests.Kit;

namespace TagLibSharp2.Tests;

/// <summary>
/// Every structure a file can nest, 100,000 levels deep, through read, a tag edit and a save.
/// </summary>
/// <remarks>
/// A file controls how deep its elements nest. A parser or writer that recurses once per level
/// ends the process with a stack overflow that no caller can catch, so each case runs in a child
/// process on a 1 MiB stack with a 1 GiB heap limit: an overflow fails one test instead of
/// aborting the run. The rows come from <see cref="DepthCases.All"/>, so a new case is covered
/// without a new test. A case that fails on main today carries the reason it is skipped.
/// </remarks>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Security")]
[DoNotParallelize]
public class NestingDepthTests
{
	static readonly TimeSpan Timeout = TimeSpan.FromMinutes (3);

	// Remove an entry when the fix lands; the row then runs and must pass.
	static readonly Dictionary<string, string> KnownOverflows = new (StringComparer.Ordinal) {
		["matroska-simpletag"] = "Matroska SimpleTag read and render recurse once per nested SimpleTag (fix in PR #151).",
		["matroska-cues"] = "Matroska Cues patching recurses once per nested CuePoint on a save that moves clusters (fix in PR #151).",
		["mp4-udta"] = "MP4 ParseBox/ParseChildren recurse once per nested container box (overflow at ~1,600 levels) and copy each payload (1 GiB by ~500 levels).",
		["mp4-track-layout"] = "MP4 ParseBox/ParseChildren recurse once per nested mdia box; CollectTrackLayout recurses on the same path.",
	};

	public static IEnumerable<TestDataRow<string>> Cases =>
		DepthCases.All.Select (c => new TestDataRow<string> (c.Name) {
			DisplayName = $"{c.Name}: {c.Structure}",
			IgnoreMessage = KnownOverflows.TryGetValue (c.Name, out var reason) ? reason : null
		});

	[TestMethod]
	[DynamicData (nameof (Cases))]
	public void HostileDepth_ReadEditAndSave_DoNotOverflowTheStack (string name)
	{
		var result = KitChild.Run (Timeout, "kit-depth", name, Nesting.HostileDepth.ToString (System.Globalization.CultureInfo.InvariantCulture));

		Assert.AreEqual (0, result.ExitCode, result.Describe ());
		Assert.Contains ($"{name} depth={Nesting.HostileDepth} ", result.Output, result.Describe ());
		Assert.IsTrue (DepthProbe.Passed (result.Output), result.Describe ());
	}

	[TestMethod]
	public void KnownOverflows_NameRealCases ()
	{
		var names = DepthCases.All.Select (c => c.Name).ToHashSet (StringComparer.Ordinal);

		foreach (var name in KnownOverflows.Keys)
			Assert.Contains (name, names);
	}
}
