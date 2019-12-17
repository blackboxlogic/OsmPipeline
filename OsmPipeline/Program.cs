using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmPipeline.Fittings;
using OsmSharp;
using System.Collections.Generic;

namespace OsmPipeline
{
	public class Program
	{
		static void Main(string[] args)
		{
			var config = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", false, true)
				.Build();
			IServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddLogging(builder => builder.AddConsole().AddFilter(level => true));
			var loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();

			//var municipalities = await FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
			//	GeoJsonAPISource.GetMunicipalities);
			//foreach (var municipality in municipalities.keys)
			//{
				var municipality = "Westbrook";
				ImportAddressesInScope(municipality, loggerFactory, config);
			//}

			Console.ReadKey(true);
		}

		static async void ImportAddressesInScope(string scopeName,
			ILoggerFactory loggerFactory, IConfigurationRoot config)
		{
			// Flesh out the Place_Type translations
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Reference.osm",
				() => Reference.Fetch(loggerFactory, scopeName));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			var conflated = FileSerializer.ReadXmlCacheOrSource(scopeName + "Conflated.osc",
				() => Conflate.Merge(loggerFactory, reference, subject, scopeName));

			//var results = await Subject.UploadChange(conflated, loggerFactory, "Importing addresses", config);
		}
	}
}
