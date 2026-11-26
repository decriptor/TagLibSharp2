// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TagLibSharp2.Image.Tiff;

/// <summary>
/// Steps of the IFD walks of an edit and of Clear on this thread (the graph walk, the Exif removal, the Apple
/// maker-note walk), for tests that bound them by an operation count.
/// </summary>
static class IfdSteps
{
	[ThreadStatic]
	internal static long Count;
}
