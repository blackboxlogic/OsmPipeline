using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OsmPipeline.Fittings;
using System.Threading.Tasks;
using System.Collections.Generic;
using static OsmPipeline.Fittings.GeoJsonAPISource;

namespace OsmPipeline
{
	public static class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
		public const string maineE911id = "maineE911id";
		public static Dictionary<string, Municipality> Municipalities;
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
			task.Wait();
			if (task.IsFaulted)
			{
				Console.WriteLine(task.Exception);
			}
			else
			{
				Console.WriteLine("Finished!");
			}
			Console.ReadKey(true);
		}

		static async Task ImportAddressesInScope()
		{
			// it seems like multiple addresses in a building isnt always a problem. probably only when:
				// conflicts with building OR multiple inside building
				// and Existing subject nodes in building
			// merge conflict resolution in command line, saving stuff to MaineMunicipalities
			// split large municipalites by zip?
			Static.Municipalities = await FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GeoJsonAPISource.GetMunicipalities);
			var municipality = ChooseMunicipality();
			var reference = await FileSerializer.ReadXmlCacheOrSource(municipality + "/Reference.osm",
				() => Reference.Fetch(municipality));
			var subject = await FileSerializer.ReadXmlCacheOrSource(municipality + "/Subject.osm",
				() => Subject.GetElementsInBoundingBox(reference.Bounds));
			var conflated = FileSerializer.WriteXml(municipality + "/Conflated.osc",
				Conflate.Merge(reference, subject, municipality));
			// This is commented out so I don't accidentally commit changes to OSM.
			//var results = await Subject.UploadChange(conflated, municipality);
			//Static.Municipalities[municipality].ChangeSetIds.Add(results);
			//Static.Municipalities[municipality].ImportDate = DateTime.UtcNow;
			//FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
		}

		static string ChooseMunicipality()
		{
			return "Westbrook";
			do
			{
				Console.WriteLine($"Which municipality?");
				var input = Console.ReadLine();
				var selection = Static.Municipalities.Keys.Where(m => m.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
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
