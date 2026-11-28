// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Gif;
using TagLibSharp2.Image.Png;
using TagLibSharp2.Image.Svg;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using TagLibSharp2.Tests.Kit;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

public partial class TiffHeaderTests
{
	[TestMethod]
	public void AsciiPointer_IsNotFollowed_AndAnArrayPastTheData_IsNotAPointer ()
	{
		var ascii = new TiffBuilder (40).Header (8).Table (8, [(0x8769, 2, 1, 1)]).ToArray ();
		Assert.IsTrue (TiffReader.TryOpen (ascii, out var text, out _));
		Assert.IsFalse (text.TryPointers (text.Entry (8, 0), out _));

		var past = new TiffBuilder (40).Header (8).Table (8, [(0x014A, 4, 5, 30)]).ToArray ();
		Assert.IsTrue (TiffReader.TryOpen (past, out var wide, out _));
		Assert.IsFalse (wide.TryPointers (wide.Entry (8, 0), out _));
	}

	[TestMethod]
	public void MotorolaOrder_RequiresBothBytes ()
	{
		Assert.IsFalse (TiffReader.IsTiff ([(byte)'M', (byte)'I', 0, 42, 0, 0, 0, 8]));
		Assert.IsTrue (TiffReader.IsTiff ([(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8]));
	}
}
