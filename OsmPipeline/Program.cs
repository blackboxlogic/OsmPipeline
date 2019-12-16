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

			ImportAddresses("Westbrook", loggerFactory, config);

			Console.ReadKey(true);
		}

		static async void ImportAddresses(string scopeName,
			ILoggerFactory loggerFactory, IConfigurationRoot config)
		{
			var municipalities = await FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GeoJsonAPISource.GetMunicipalities);
			// Loggers/config everywhere
			// Split by bounding box instead of municipality.
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Reference.osm",
				() => Reference.Fetch(loggerFactory, scopeName));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			// Apply a node to an encompasing building if it doesn't conflict and there is nothing else in the building
			var conflated = FileSerializer.ReadXmlCacheOrSource(scopeName + "Conflated.osc",
				() => Conflate.Merge(loggerFactory, reference, subject, scopeName));

			//var results = await Subject.UploadChange(conflated, loggerFactory, "Importing addresses", config);
		}
	}
}
