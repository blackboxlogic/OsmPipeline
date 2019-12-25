using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmPipeline.Fittings;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OsmPipeline
{
	public static class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
		public static string maineE911id = "maineE911id";
	}

	public class Program
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

			var task = ImportAddressesInScope();

			Console.WriteLine("Finished!");
			Console.ReadKey(true);
		}

		static async Task ImportAddressesInScope()
		{
			var municipalities = await FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GeoJsonAPISource.GetMunicipalities);
			var municipality = UserChooseOption(municipalities.Keys, "municipality");
			var reference = await FileSerializer.ReadXmlCacheOrSource(municipality + "/Reference.osm",
				() => Reference.Fetch(municipality));
			var subject = await FileSerializer.ReadXmlCacheOrSource(municipality + "/Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			var conflated = FileSerializer.ReadXmlCacheOrSource(municipality + "/Conflated.osc",
				() => Conflate.Merge(reference, subject, municipality));

			// This is commented out so I don't accidentally commit changes to OSM.
			//var results = await Subject.UploadChange(conflated, municipality);
		}

		static string UserChooseOption(IEnumerable<string> options, string optionName = "option")
		{
			do
			{
				Console.WriteLine($"Which {optionName}?");
				var input = Console.ReadLine();
				var selection = options.Where(m => m.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
				if (selection.Length == 1)
				{
					return selection[0];
				}
				else
				{
					Console.Write(string.Join("\n", selection));
				}
			} while (true);
		}
	}
}
