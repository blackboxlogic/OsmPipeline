using System;
using System.Linq;
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
			IServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddLogging(builder => builder.AddConsole().AddFilter(level => true));
			Static.LogFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();

			Environment.CurrentDirectory += Static.Config["WorkingDirectory"];

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
			// Should municpality be trusted, even for r7 t2? Could leave city off I guess.
			// Flesh out the Place_Type translations
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "/Reference.osm",
				() => Reference.Fetch(scopeName));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "/Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			var conflated = FileSerializer.ReadXmlCacheOrSource(scopeName + "/Conflated.osc",
				() => Conflate.Merge(reference, subject, scopeName));

			//var results = await Subject.UploadChange(conflated, loggerFactory, "Importing E911 addresses in " + scopeName, "Maine_E911_Addresses_Roads_PSAP", config);
		}
	}
}
