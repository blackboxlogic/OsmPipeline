using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Complete;
using OsmSharp.Streams;

namespace MaineRoads
{
	public static partial class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
	}

	class Program
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

			try
			{
				Reference.Generate();
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			Console.ReadKey(true);
		}

		

	}
}
