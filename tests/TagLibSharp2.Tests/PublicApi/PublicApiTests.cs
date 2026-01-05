// Copyright (c) 2025 Stephen Shaw and contributors

using PublicApiGenerator;

namespace TagLibSharp2.Tests.PublicApi;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("PublicApi")]
public class PublicApiTests : VerifyBase
{
	[TestMethod]
	public Task PublicApi_HasNotChanged ()
	{
		var assembly = typeof (TagLibSharp2.Core.BinaryData).Assembly;

		var options = new ApiGeneratorOptions {
			ExcludeAttributes = [
				"System.Runtime.Versioning.TargetFrameworkAttribute",
				"System.Reflection.AssemblyMetadataAttribute",
				"System.Runtime.CompilerServices.InternalsVisibleToAttribute"
			]
		};

		var publicApi = assembly.GeneratePublicApi (options);

		var settings = new VerifySettings ();
		settings.UseDirectory ("Snapshots");
		settings.UseFileName ("TagLibSharp2.PublicApi");

		return Verify (publicApi, settings);
	}
}
