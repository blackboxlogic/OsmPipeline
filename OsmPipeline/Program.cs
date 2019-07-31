using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using OsmSharp.IO.API;

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

		private const ulong maine = 63512 + 3600000000;
		private const ulong westbrook = 132501 + 3600000000;
		private const ulong frenchtown = 707032430 + 2400000000;

		static void Main(string[] args)
		{
			var osm = Open().Result;
			var change = Edit(osm, EditGenerator, EditVersion).Result;
			Save(change, ChangeComment, ChangeCreatedBy);
			Console.ReadKey(true);
		}

		private static async Task<Osm> Open()
		{
			string query =
@"node
  ['phone'~'^([^0-9]*1)?([^0-9]*[0-9]){10}[^0-9]*$']
  ['phone'!~'^\\+1\\-[0-9]{3}\\-[0-9]{3}\\-[0-9]{4}$']
  (area: " + westbrook + @");
out meta;";
			var osm = await OverpassAPI.Fetch(query);
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
				var fixedPhone = Phones.Fix(number);
				Console.WriteLine($"{element.Id}:\t{number}\t-->\t{fixedPhone}");
				element.Tags["phone"] = fixedPhone;

				modify.Add(element);
			}

			// TODO: Implement 'if-unused'
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
			var changesetId = await basic.CreateChangeset(comment); //, createdBy);
			ValidateChangeset(change, changesetId);
			var diffResult = await basic.UploadChangeset(changesetId, change);
			// Should I check the diffResult?
			await basic.CloseChangeset(changesetId);
		}
		private static void ValidateChangeset(OsmChange change, string changesetId)
		{
			var changeCount = change.Modify?.Length ?? 0
				+ change.Delete?.Length ?? 0
				+ change.Create?.Length ?? 0;

			if (changeCount > MaxChangesPerChangeSet)
			{
				throw new Exception($"Too many changes in this changeset. {changeCount} > {MaxChangesPerChangeSet}");
			}

			if (new OsmGeo[][] { change.Delete, change.Modify }
				.Where(a => a != null)
				.SelectMany(a => a)
				.Any(e => !e.Tags.ContainsKey("version")))
			{
				throw new Exception("All Delete and Modify elements must contain a 'version' tag.");
			}
		}
	}
}
