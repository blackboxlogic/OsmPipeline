using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmPipeline.Fittings;
using System.Threading.Tasks;

namespace OsmPipeline
{
	public static class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
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

			var municipality = SelectMunicipality().Result;
			ImportAddressesInScope(municipality);

			Console.ReadKey(true);
		}

		static async Task<string> SelectMunicipality()
		{
			//return "Westbrook";
			var municipalities = await FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GeoJsonAPISource.GetMunicipalities);

			do
			{
				Console.WriteLine("Which municipality?");
				var input = Console.ReadLine();
				var selection = municipalities.Keys.Where(m => m.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
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

		static async void ImportAddressesInScope(string scopeName)
		{
			// Should municpality be trusted, even for r7 t2? Could leave city off I guess?
			// A place to record progress.
			var reference = await FileSerializer.ReadXmlCacheOrSource(scopeName + "/Reference.osm",
				() => Reference.Fetch(scopeName));
			var subject = await FileSerializer.ReadXmlCacheOrSource(scopeName + "/Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			var conflated = FileSerializer.ReadXmlCacheOrSource(scopeName + "/Conflated.osc",
				() => Conflate.Merge(reference, subject, scopeName));

			//var results = await Subject.UploadChange(conflated,
			//	"Importing addresses in " + scopeName, Static.Config["DataSourceName"]);
		}
	}
}
