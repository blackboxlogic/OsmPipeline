using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmPipeline.Fittings;

namespace OsmPipeline
{
	public static class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
	}

	public class Program
	{
		static void Main(string[] args)
		{
			Static.Config = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", false, true)
				.Build();
			Environment.CurrentDirectory += Static.Config["WorkingDirectory"];
			IServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddLogging(builder => builder.AddConsole().AddFilter(level => true));
			Static.LogFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();

			//var municipalities = await FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
			//	GeoJsonAPISource.GetMunicipalities);
			//foreach (var municipality in municipalities.keys)
			//{
				var municipality = "Westbrook";
				ImportAddressesInScope(municipality);
			//}

			Console.ReadKey(true);
		}

		static async void ImportAddressesInScope(string scopeName)
		{
			// Make PLACE_TYPE optional?
			// Throw exceptions on address points added IN a building that they don't match?
			// Should municpality be trusted, even for r7 t2? Could leave city off I guess?
			// OH SHOOT, matching with relations? calculating their centroid?
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "/Reference.osm",
				() => Reference.Fetch(scopeName));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "/Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			var conflated = FileSerializer.ReadXmlCacheOrSource(scopeName + "/Conflated.osc",
				() => Conflate.Merge(reference, subject, scopeName));

			//var results = await Subject.UploadChange(conflated,
			//	"Importing addresses in " + scopeName, Static.Config["DataSourceName"]);
		}
	}
}
