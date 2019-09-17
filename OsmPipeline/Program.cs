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

			Phones.FixPhones(loggerFactory, config, Locations.Maine);

			//var logger = loggerFactory.CreateLogger("OsmApiEnder");
			//var osmApiEnder = new OsmApiEnder(logger, OsmApiUrl, OsmUsername, OsmPassword, changeTags);
			//var source = new Fittings.KmlFileSource(@"C:\Users\Alex\Desktop\MaineE911.kml");
			//var LoggingEnder = new Fittings.LoggingEnder(loggerFactory.CreateLogger("kml"));
			//var change = Edit(osm, EditGenerator, EditVersion).Result;
			//Save(change, ChangeComment, ChangeCreatedBy);
			Console.ReadKey(true);
		}
	}
}
