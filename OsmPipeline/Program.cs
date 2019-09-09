using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using OsmSharp.IO.API;
using OsmSharp.Tags;
using System.Xml.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsmPipeline.Fittings;
using static OsmPipeline.Fittings.OverpassQuery;
using Microsoft.Extensions.Configuration;

namespace OsmPipeline
{
	class Program
	{
		private const int MaxChangesPerChangeSet = 10000;
		private const double EditVersion = .1;
		private const string OsmApiUrl = "https://master.apis.dev.openstreetmap.org/api/0.6/";
		// TODO: Fetch creds from a keyvault? from a config file? or?
		private const string OsmUsername = "blackboxlogic+dev@gmail.com";
		private const string OsmPassword = "********";

		private static Relation USA = new Relation() { Id = 9331155 };
		private static Relation Maine = new Relation() { Id = 63512 };
		private static Relation Westbrook = new Relation() { Id = 132501 };
		private static Way Frenchtown = new Way() { Id = 707032430 };

		private static string Tag = "phone";

		static void Main(string[] args)
		{
			var Config = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", false, true)
				.Build();

			IServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddLogging(builder => builder.AddConsole().AddFilter(level => true));
			var loggerFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();

			// Build
			var logger = new Fittings.Log<OsmGeo>(loggerFactory.CreateLogger(typeof(OsmGeo)));
			var changeTags = new TagsCollection()
			{
				new Tag("comment", ""),
				new Tag("created_by", nameof(OsmPipeline)),
				new Tag("bot", "yes"),
				new Tag("source", "")
			};
			var osmApi = new OsmApiEnder(loggerFactory.CreateLogger(typeof(OsmApiEnder)),
				Config["osmApiUrl"], Config["basicAuth:User"], Config["basicAuth:Password"], changeTags);
			// Pull
			osmApi.Put(
				FileSerializer.ReadCacheOrSource(
					"change.xml",
					() => Translate.GeosToChange(null,
						FixPhones(
							Translate.OsmToGeos(
								FileSerializer.ReadCacheOrSource(
									"overpassApiSource.xml",
									() => OverpassApi.Get(GetOPQuery(Westbrook)))),
							e => logger.LogItem(e)),
						null,
						"OsmPipeline")));
			


			//var logger = loggerFactory.CreateLogger("OsmApiEnder");
			//var osmApiEnder = new OsmApiEnder(logger, OsmApiUrl, OsmUsername, OsmPassword, changeTags);
			//var source = new Fittings.KmlFileSource(@"C:\Users\Alex\Desktop\MaineE911.kml");
			//var LoggingEnder = new Fittings.LoggingEnder(loggerFactory.CreateLogger("kml"));
			//var change = Edit(osm, EditGenerator, EditVersion).Result;
			//Save(change, ChangeComment, ChangeCreatedBy);
			Console.ReadKey(true);
		}

		private static string GetOPQuery(OsmGeo where)
		{
			var subQueries = new[] {
				Query(OverpassGeoType.Node, where,
					new Filter(Tag, Comp.Like, Phones.HasAnything),
					new Filter(Tag, Comp.NotLike, Phones.Correct)),
				Query(OverpassGeoType.Way, where,
					new Filter(Tag, Comp.Like, Phones.HasAnything),
					new Filter(Tag, Comp.NotLike, Phones.Correct)),
				Query(OverpassGeoType.Relation, where,
					new Filter(Tag, Comp.Like, Phones.HasAnything),
					new Filter(Tag, Comp.NotLike, Phones.Correct))
			};
			var query = Union(subQueries);
			query = AddOut(query, true);
			return query;
		}

		private static IEnumerable<OsmGeo> FixPhones(IEnumerable<OsmGeo> elements, Action<OsmGeo> Reject = null)
		{
			List<OsmGeo> modify = new List<OsmGeo>();

			foreach (var element in elements)
			{
				if (!element.Tags.TryGetValue(Tag, out string value))
					continue;
				// https://en.wikipedia.org/wiki/List_of_country_calling_codes
				if (Phones.TryFix(value, new[] { "207", "800", "888", "877" }, "207", out string fixedValue))
				{
					Console.WriteLine($"{element.Type}-{element.Id}.{Tag}:\t{value}\t-->\t{fixedValue}");
					element.Tags[Tag] = fixedValue;
					yield return element;
				}
				else
				{
					Reject?.Invoke(element);
				}
			}
		}
	}
}
