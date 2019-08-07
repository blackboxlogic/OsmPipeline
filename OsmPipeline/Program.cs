using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using OsmSharp.IO.API;
using OsmSharp.Tags;

namespace OsmPipeline
{
	class Program
	{
		private const int MaxChangesPerChangeSet = 10000;
		private const string EditGenerator = "OsmPipeline";
		private const double EditVersion = .1;
		private const string OsmApiUrl = "https://master.apis.dev.openstreetmap.org/api/0.6/";
		private const string OsmUsername = "blackboxlogic+dev@gmail.com";
		private const string OsmPassword = "********";
		private const string ChangeComment = EditGenerator;
		private const string ChangeCreatedBy = EditGenerator;

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

		private static async Task<Osm> Open()
		{
			var nodePhone10 = OverpassAPI.Query(OsmGeoType.Node, Maine, OverpassAPI.Phone10DigitStartsWith207);
			var nodePhone11 = OverpassAPI.Query(OsmGeoType.Node, Maine, OverpassAPI.Phone11DigitStartWith1, OverpassAPI.PhoneNotCorrectFormat);
			var wayPhone10 = OverpassAPI.Query(OsmGeoType.Way, Maine, OverpassAPI.Phone10DigitStartsWith207);
			var wayPhone11 = OverpassAPI.Query(OsmGeoType.Way, Maine, OverpassAPI.Phone11DigitStartWith1, OverpassAPI.PhoneNotCorrectFormat);
			var relPhone10 = OverpassAPI.Query(OsmGeoType.Relation, Maine, OverpassAPI.Phone10DigitStartsWith207);
			var relPhone11 = OverpassAPI.Query(OsmGeoType.Relation, Maine, OverpassAPI.Phone11DigitStartWith1, OverpassAPI.PhoneNotCorrectFormat);
			var query = OverpassAPI.Union(nodePhone10, nodePhone11, wayPhone10, wayPhone11, relPhone10, relPhone11);
			query = OverpassAPI.AddOut(query, true);
			var osm = await OverpassAPI.Execute(query);
			return osm;
		}

		private static async Task<OsmChange> Edit(Osm osm, string generator, double? version)
		{
			List<OsmGeo> modify = new List<OsmGeo>();
			List<OsmGeo> delete = new List<OsmGeo>();
			List<OsmGeo> create = new List<OsmGeo>();

			var elements = new OsmGeo[][] { osm.Nodes, osm.Ways, osm.Relations }
				.Where(a => a != null)
				.SelectMany(a => a);

			foreach (var element in elements)
			{
				var number = element.Tags["phone"];
				if (Phones.TryFix(number, out string fixedPhone))
				{
					Console.WriteLine($"{element.Type}-{element.Id}:\t{number}\t-->\t{fixedPhone}");
					element.Tags["phone"] = fixedPhone;

					modify.Add(element);
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
