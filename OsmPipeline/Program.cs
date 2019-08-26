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

namespace OsmPipeline
{
	class Program
	{
		private const int MaxChangesPerChangeSet = 10000;
		private const string EditGenerator = "OsmPipeline";
		private const double EditVersion = .1;
		private const string OsmApiUrl = "https://master.apis.dev.openstreetmap.org/api/0.6/";
		// TODO: Fetch creds from a keyvault? from a config file? or?
		private const string OsmUsername = "blackboxlogic+dev@gmail.com";
		private const string OsmPassword = "********";
		private const string ChangeComment = EditGenerator;
		private const string ChangeCreatedBy = EditGenerator;

		private static Relation USA = new Relation() { Id = 9331155 };
		private static Relation Maine = new Relation() { Id = 63512 };
		private static Relation Westbrook = new Relation() { Id = 132501 };
		private static Way Frenchtown = new Way() { Id = 707032430 };

		static void Main(string[] args)
		{
			var osm = Open().Result;
			var change = Edit(osm, EditGenerator, EditVersion).Result;
			//Save(change, ChangeComment, ChangeCreatedBy);
			Console.ReadKey(true);
		}

		private static async Task<Osm> Open(bool withCache = true)
		{
			var subQueries = new[] {
				OverpassAPI.Query(OsmGeoType.Node, Maine,
					new Filter("phone", Comp.Like, Phones.HasAnything),
					new Filter("phone", Comp.NotLike, Phones.Correct)),
				OverpassAPI.Query(OsmGeoType.Way, Maine,
					new Filter("phone", Comp.Like, Phones.HasAnything),
					new Filter("phone", Comp.NotLike, Phones.Correct)),
				OverpassAPI.Query(OsmGeoType.Relation, Maine,
					new Filter("phone", Comp.Like, Phones.HasAnything),
					new Filter("phone", Comp.NotLike, Phones.Correct))
			};
			var query = OverpassAPI.Union(subQueries);
			query = OverpassAPI.AddOut(query, true);

			var serializer = new XmlSerializer(typeof(Osm));
			if (withCache && File.Exists($"Overpass.txt"))
			{
				using (var fileStream = File.OpenRead($"Overpass.txt"))
				{
					return (Osm)serializer.Deserialize(fileStream);
				}
			}


			var osm = await OverpassAPI.Execute(query);

			if (withCache)
			{
				using (var fileStream = File.OpenWrite($"Overpass.txt"))
				{
					serializer.Serialize(fileStream, osm);
				}
			}

			return osm;
		}

		private static async Task<OsmChange> Edit(Osm osm, string generator, double? version)
		{
			List<OsmGeo> modify = new List<OsmGeo>();
			List<OsmGeo> delete = new List<OsmGeo>();
			List<OsmGeo> create = new List<OsmGeo>();
			List<OsmGeo> skip = new List<OsmGeo>();
			var elements = new OsmGeo[][] { osm.Nodes, osm.Ways, osm.Relations }
				.Where(a => a != null)
				.SelectMany(a => a);
			var tag = "phone";

			foreach (var element in elements)
			{
				if (!element.Tags.TryGetValue(tag, out string value))
					continue;
				// https://en.wikipedia.org/wiki/List_of_country_calling_codes
				if (Phones.TryFix(value, new[] { "207", "800", "888", "877" }, out string fixedValue))
				{
					Console.WriteLine($"{element.Type}-{element.Id}.{tag}:\t{value}\t-->\t{fixedValue}");
					element.Tags[tag] = fixedValue;
					modify.Add(element);
				}
				else
				{
					skip.Add(element);
				}
			}

			Console.WriteLine($"{modify.Count + delete.Count + create.Count} changes.");
			Console.WriteLine($"{skip.Count} skipped.");

			foreach (var element in skip)
			{
				if (element.Tags.TryGetValue(tag, out string value))
				{
					Console.WriteLine($"{element.Type}-{element.Id}.{tag}:\t{value}\t-->\tCan not fix!");
				}
			}

			var change = new OsmChange()
			{
				Generator = generator,
				Version = version,
				Modify = modify.ToArray(),
				Delete = delete.ToArray(),
				Create = create.ToArray()
			};

			using (var fileStream = File.OpenWrite("change.txt"))
			{
				var serializer = new XmlSerializer(typeof(OsmChange));
				serializer.Serialize(fileStream, change);
			}

			return change;
		}

		private static async void Save(OsmChange change, string comment, string createdBy)
		{
			BasicAuthClient basic = new BasicAuthClient(
				OsmApiUrl, OsmUsername, OsmPassword);
			var changeSetTags = new TagsCollection()
			{
				new Tag("comment", comment),
				new Tag("created_by", createdBy),
				new Tag("bot", "yes")
			};
			var changesetId = await basic.CreateChangeset(changeSetTags);
			var diffResult = await basic.UploadChangeset(changesetId, change);
			// What should I do with the diffResult?
			await basic.CloseChangeset(changesetId);
		}
	}
}
