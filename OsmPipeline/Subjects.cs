using OsmSharp.API;
using OsmSharp;
using System.Threading.Tasks;
using OsmPipeline.Fittings;
using OsmSharp.IO.API;
using OsmSharp.Changesets;
using OsmSharp.Tags;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace OsmPipeline
{
	public static class Subjects
	{
		private static ILogger Log;

		static Subjects()
		{
			Log = Static.LogFactory.CreateLogger("Subject");
		}

		public static async Task<Bounds> GetBoundingBox(OsmGeo scope, IConfigurationRoot config)
		{
			var nominatim = new OsmNominatimClient(config["CreatedBy"], config["NominatimUrl"], config["Email"]);
			var target = await nominatim.Lookup(scope.Type.ToString()[0].ToString() + scope.Id.Value);
			return new Bounds()
			{
				MinLatitude = target[0].boundingbox[0],
				MaxLatitude = target[0].boundingbox[1],
				MinLongitude = target[0].boundingbox[2],
				MaxLongitude = target[0].boundingbox[3]
			};
		}

		public static Osm GetElementsInBoundingBox(params Bounds[] bounds)
		{
			Log.LogInformation("Fetching Subject material from OSM");
			var osmApiClient = new NonAuthClient(Static.Config["OsmApiUrl"], Static.HttpClient,
				Static.LogFactory.CreateLogger<NonAuthClient>());

			var answers = new List<Osm>();
			foreach (var bound in bounds)
			{
				answers.Add(GetElementsInBoundingBox(bound, osmApiClient).Result);
			}
			return answers.Merge();

			//var tasks = bounds.Select(async bound => await GetElementsInBoundingBox(bound, osmApiClient)).ToArray();
			//Task.WaitAll(tasks);
			//return tasks.Select(t => t.Result).Merge();
		}

		private static async Task<Osm> GetElementsInBoundingBox(Bounds bounds, NonAuthClient client, int depth = 0, int maxDepth = 3)
		{
			try
			{
				return await client.GetMap(bounds);
			}
			catch (OsmApiException e)
			{
				if (!e.Message.Contains("limit is 50000") || depth > maxDepth) throw;

				Log.LogInformation("Fetch area was too big. Splitting it into smaller pieces and re-trying.");
				var tasks = bounds.Quarter().Select(async quarter => await GetElementsInBoundingBox(quarter, client, depth + 1)).ToArray();
				Task.WaitAll(tasks);
				return tasks.Select(t => t.Result).Merge();
			}
		}

		public static TagsCollection GetCommitTags(string municipality)
		{
			return new TagsCollection()
			{
				new Tag("comment", Static.Config["CommitComment"] + " (" + municipality + ")"),
				new Tag("created_by", "OsmPipeline"),
				new Tag("created_by_library", Static.Config["created_by_library"]),
				new Tag("municipality", municipality),
				new Tag("osm_wiki_documentation_page", Static.Config["osm_wiki_documentation_page"]),
				new Tag("bot", "yes"),
				new Tag("source", Static.Config["DataSourceName"])
			};
		}

		public static async Task<long[]> UploadChange(string changeFile, string description)
		{
			var change = FileSerializer.ReadXml<OsmChange>(changeFile);
			Log.LogInformation("Uploading change to OSM");
			var changeTags = GetCommitTags(description);
			changeTags.RemoveKey("municipality");
			var osmApiClient = new BasicAuthClient(Static.HttpClient,
				Static.LogFactory.CreateLogger<BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);
			var changeParts = change.SplitByCount().ToArray();
			var result = new long[changeParts.Length];
			int i = 0;

			foreach (var part in changeParts)
			{
				if (changeParts.Length > 1) changeTags.AddOrReplace("change-part", $"part {i + 1} of {changeParts.Length}");
				result[i] = await UploadChangePart(part, changeTags, osmApiClient, $"{description}/Uploaded/");
				i++;
			}

			return result;
		}

		public static async Task<long[]> UploadChange(OsmChange change, string municipality)
		{
			Log.LogInformation("Uploading change to OSM");
			var changeTags = GetCommitTags(municipality);
			var osmApiClient = new BasicAuthClient(Static.HttpClient,
				Static.LogFactory.CreateLogger<BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);
			var changeParts = change.SplitByCount().ToArray();
			var result = new long[changeParts.Length];
			int i = 0;

			foreach (var part in changeParts)
			{
				if (changeParts.Length > 1) changeTags.AddOrReplace("change-part", $"part {i+1} of {changeParts.Length}");
				result[i] = await UploadChangePart(part, changeTags, osmApiClient, $"{municipality}/Uploaded/");
				i++;
			}

			return result;
		}

		public static async Task<long[]> UploadChange(OsmChange change, TagsCollection changeTags, string pathForFiles)
		{
			Log.LogInformation("Uploading change to OSM");
			var osmApiClient = new BasicAuthClient(Static.HttpClient,
				Static.LogFactory.CreateLogger<BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);
			var changeParts = change.SplitByCount().ToArray();
			var result = new long[changeParts.Length];
			int i = 0;

			foreach (var part in changeParts)
			{
				if (changeParts.Length > 1) changeTags.AddOrReplace("change-part", $"part {i + 1} of {changeParts.Length}");
				result[i] = await UploadChangePart(part, changeTags, osmApiClient, pathForFiles);
				i++;
			}

			return result;
		}

		private static async Task<long> UploadChangePart(this OsmChange part,
			TagsCollection tags, BasicAuthClient client, string pathForFiles)
		{
			var changeSetId = await client.CreateChangeset(tags);
			Log.LogDebug($"Creating ChangeSet {changeSetId}");
			FileSerializer.WriteXml(Path.Combine(pathForFiles, $"{changeSetId}-Conflated.osc"), part);
			var diffResult = await client.UploadChangeset(changeSetId, part);
			FileSerializer.WriteXml(Path.Combine(pathForFiles, $"{changeSetId}-DiffResult.diff"), diffResult);
			await client.CloseChangeset(changeSetId);
			Log.LogDebug($"Closing ChangeSet {changeSetId}");
			return changeSetId;
		}

		public static async Task<long> CreateNote(double lat, double lon, string message)
		{
			var client = new BasicAuthClient(Static.HttpClient,
				Static.LogFactory.CreateLogger<BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);
			var note = await client.CreateNote((float)lat, (float)lon, message); // remove cast after next osmApiClient version is released
			return note.Id.Value;
		}
	}
}