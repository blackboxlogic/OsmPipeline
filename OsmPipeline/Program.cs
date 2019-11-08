using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmPipeline.Fittings;
using OsmSharp;

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

			ImportAddresses(Scopes.Westbrook, nameof(Scopes.Westbrook), loggerFactory, config);

			Console.ReadKey(true);
		}

		static async void ImportAddresses(OsmGeo scope, string scopeName,
			ILoggerFactory loggerFactory, IConfigurationRoot config)
		{
			// Loggers/config everywhere
			// Split by bounding box instead of municipality.
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Reference.osm",
				() => Reference.Fetch(loggerFactory, scopeName));
			// Maybe choose bounds based on the referece, not the subject
			var bounds = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Bounds.xml",
				() => Subject.GetBoundingBox(scope, config));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Subject.osm",
				() => Subject.GetElementsInBoundingBox(bounds));
			// Apply a node to an encompasing building if it doesn't conflict
			var conflated = FileSerializer.ReadXmlCacheOrSource(scopeName + "Conflated.osm",
				() => Conflate.Merge(loggerFactory, reference, subject));
			var results = await Subject.UploadChange(conflated, loggerFactory, "Importing addresses", config);
		}
	}
}
