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
			Addresses.ValidateAddresses(loggerFactory, config, Locations.Frenchtown);

			Console.ReadKey(true);
		}
	}
}
