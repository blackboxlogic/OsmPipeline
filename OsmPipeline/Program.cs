using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmSharp.Tags;
using OsmPipeline.Fittings;

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

			//Phones.FixPhones(loggerFactory, config, Locations.Maine);

			var scope = Scopes.Westbrook;
			var scopeName = nameof(Scopes.Westbrook);

			var reference = FileSerializer.ReadXmlCacheOrSource(scopeName + "Reference.osm",
				() => Addresses.ValidateAddresses(loggerFactory, config, scope, scopeName)).Result;

			// Conflate
			var osmChange = Conflate.FetchAndMerge(scope, scopeName, reference).Result;

			// upload
			//var osmApiEnder = new OsmApiEnder(logger, OsmApiUrl, OsmUsername, OsmPassword, changeTags);
			//var change = Edit(osm, EditGenerator, EditVersion).Result;

			Console.ReadKey(true);
		}
	}
}
