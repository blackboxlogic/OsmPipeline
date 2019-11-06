using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmSharp.Tags;
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

			ImportAddresses(Scopes.Westbrook, nameof(Scopes.Westbrook), loggerFactory);

			Console.ReadKey(true);
		}

		static async void ImportAddresses(OsmGeo scope, string scopeName, ILoggerFactory loggerFactory)
		{
			// room has unit*
			// Split by bounding box instead of municipality.
			// invalidate downstream caches
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Reference.osm",
				() => Reference.Fetch(loggerFactory, scope, scopeName));
			var bounds = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Bounds.xml",
				() => Conflate.GetBoundingBox(scope));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Subject.osm",
				() => Conflate.GetElementsInBoundingBox(bounds));
			var conlfated = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Conflated.osm",
				() => Conflate.Merge(loggerFactory, reference, subject));
			// upload
			//var osmApiEnder = new OsmApiEnder(logger, OsmApiUrl, OsmUsername, OsmPassword, changeTags);
			//var change = Edit(osm, EditGenerator, EditVersion).Result;
		}

		static void FixPhones(ILoggerFactory loggerFactory, IConfigurationRoot config, OsmGeo scope)
		{
			Phones.FixPhones(loggerFactory, config, scope);
		}
	}
}
