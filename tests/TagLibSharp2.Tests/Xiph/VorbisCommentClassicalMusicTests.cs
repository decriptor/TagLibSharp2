// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Xiph;

/// <summary>
/// Tests for classical music metadata (Work, Movement, MovementNumber, MovementTotal) in Vorbis Comments.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
public sealed class VorbisCommentClassicalMusicTests
{
	[TestMethod]
	public void Work_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ("Test Vendor");

		comment.Work = "Symphony No. 9 in D minor, Op. 125";

		Assert.AreEqual ("Symphony No. 9 in D minor, Op. 125", comment.Work);
	}

	[TestMethod]
	public void Work_Render_PreservesValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.Work = "Piano Concerto No. 5";

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Piano Concerto No. 5", parsed.Tag!.Work);
	}

	[TestMethod]
	public void Movement_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ("Test Vendor");

		comment.Movement = "Allegro con brio";

		Assert.AreEqual ("Allegro con brio", comment.Movement);
	}

	[TestMethod]
	public void Movement_Render_PreservesValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.Movement = "Adagio sostenuto";

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("Adagio sostenuto", parsed.Tag!.Movement);
	}

	[TestMethod]
	public void MovementNumber_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ("Test Vendor");

		comment.MovementNumber = 3;

		Assert.AreEqual (3u, comment.MovementNumber);
	}

	[TestMethod]
	public void MovementNumber_Render_PreservesValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.MovementNumber = 2;

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (2u, parsed.Tag!.MovementNumber);
	}

	[TestMethod]
	public void MovementTotal_GetSet_RoundTrips ()
	{
		var comment = new VorbisComment ("Test Vendor");

		comment.MovementTotal = 4;

		Assert.AreEqual (4u, comment.MovementTotal);
	}

	[TestMethod]
	public void MovementTotal_Render_PreservesValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.MovementTotal = 5;

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual (5u, parsed.Tag!.MovementTotal);
	}

	[TestMethod]
	public void AllClassicalMetadata_RoundTrip_PreservesAll ()
	{
		var comment = new VorbisComment ("Test");
		comment.Work = "The Planets, Op. 32";
		comment.Movement = "Mars, the Bringer of War";
		comment.MovementNumber = 1;
		comment.MovementTotal = 7;

		var rendered = comment.Render ();
		var parsed = VorbisComment.Read (rendered.Span);

		Assert.IsTrue (parsed.IsSuccess);
		Assert.AreEqual ("The Planets, Op. 32", parsed.Tag!.Work);
		Assert.AreEqual ("Mars, the Bringer of War", parsed.Tag.Movement);
		Assert.AreEqual (1u, parsed.Tag.MovementNumber);
		Assert.AreEqual (7u, parsed.Tag.MovementTotal);
	}

	[TestMethod]
	public void Work_SetNull_ClearsValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.Work = "Symphony No. 5";

		comment.Work = null;

		Assert.IsNull (comment.Work);
	}

	[TestMethod]
	public void Movement_SetNull_ClearsValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.Movement = "Allegro";

		comment.Movement = null;

		Assert.IsNull (comment.Movement);
	}

	[TestMethod]
	public void MovementNumber_SetNull_ClearsValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.MovementNumber = 1;

		comment.MovementNumber = null;

		Assert.IsNull (comment.MovementNumber);
	}

	[TestMethod]
	public void MovementTotal_SetNull_ClearsValue ()
	{
		var comment = new VorbisComment ("Test");
		comment.MovementTotal = 4;

		comment.MovementTotal = null;

		Assert.IsNull (comment.MovementTotal);
	}

	[TestMethod]
	public void MovementTotal_SetNull_ClearsMovementCount ()
	{
		var comment = new VorbisComment ("Test");
		comment.SetValue ("MOVEMENTCOUNT", "4");

		comment.MovementTotal = null;

		Assert.IsNull (comment.MovementTotal);
		Assert.IsNull (comment.GetValue ("MOVEMENTTOTAL"));
		Assert.IsNull (comment.GetValue ("MOVEMENTCOUNT"));
	}

	[TestMethod]
	public void MovementTotal_Set_RemovesMovementCount ()
	{
		var comment = new VorbisComment ("Test");
		comment.SetValue ("MOVEMENTCOUNT", "4");

		comment.MovementTotal = 6;

		Assert.AreEqual (6u, comment.MovementTotal);
		Assert.AreEqual ("6", comment.GetValue ("MOVEMENTTOTAL"));
		Assert.IsNull (comment.GetValue ("MOVEMENTCOUNT"));
	}

	[TestMethod]
	public void MovementTotal_UnusableMovementTotal_FallsBackToMovementCount ()
	{
		var comment = new VorbisComment ("Test");
		comment.SetValue ("MOVEMENTTOTAL", "abc");
		comment.SetValue ("MOVEMENTCOUNT", "4");

		Assert.AreEqual (4u, comment.MovementTotal);
	}

	[TestMethod]
	public void MovementTotal_EmptyMovementTotal_FallsBackToMovementCount ()
	{
		var comment = new VorbisComment ("Test");
		comment.SetValue ("MOVEMENTTOTAL", "");
		comment.SetValue ("MOVEMENTCOUNT", "4");

		Assert.AreEqual (4u, comment.MovementTotal);
	}
}
