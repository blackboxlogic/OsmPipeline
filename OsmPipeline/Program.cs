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
			// Loggers/config everywhere
			// Split by bounding box instead of municipality.
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Reference.osm",
				() => Reference.Fetch(loggerFactory, scopeName));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			// Apply a node to an encompasing building if it doesn't conflict and there is nothing else in the building
			var conflated = FileSerializer.ReadXmlCacheOrSource(scopeName + "Conflated.osmChange",
				() => Conflate.Merge(loggerFactory, reference, subject));
			
			FileSerializer.WriteXml(scopeName + "Conflated.Create.osm", conflated.Create.AsOsm());
			var wayNodes = new HashSet<long>(conflated.Modify.OfType<Way>().SelectMany(w => w.Nodes));
			var modifyAndWayNodes = conflated.Modify.Concat(subject.Nodes.Where(n => wayNodes.Contains(n.Id.Value)));
			FileSerializer.WriteXml(scopeName + "Conflated.Modify.osm", modifyAndWayNodes.AsOsm());//conflated.Modify.AsOsm());

			//var results = await Subject.UploadChange(conflated, loggerFactory, "Importing addresses", config);
		}
	}
}
