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

		public static async Task<Osm> GetElementsInBoundingBox(Bounds bounds)
		{
				Log.LogInformation("Fetching Subject material from OSM");
				var osmApiClient = new NonAuthClient(Static.Config["OsmApiUrl"], Static.HttpClient,
					Static.LogFactory.CreateLogger<NonAuthClient>());
				return await GetElementsInBoundingBox(bounds, osmApiClient);
		}

		private static async Task<Osm> GetElementsInBoundingBox(Bounds bounds, NonAuthClient client, int depth = 0)
		{
			try
			{
				return await client.GetMap(bounds);
			}
			catch (Exception e)
			{
				if (!e.Message.Contains("limit is 50000") || depth > 3) throw;

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

		public static async Task<long[]> UploadChange(OsmChange change, string municipality)
		{
			Log.LogInformation("Uploading change to OSM");
			var changeTags = GetCommitTags(municipality);
			var osmApiClient = new BasicAuthClient(Static.HttpClient,
				Static.LogFactory.CreateLogger<BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);
			var changeParts = change.Split().ToArray();
			var result = new List<long>();
			foreach (var part in changeParts)
			{
				result.Add(await UploadChangePart(part, changeTags, osmApiClient, municipality));
			}
			return result.ToArray();
		}

		private static async Task<long> UploadChangePart(this OsmChange part,
			TagsCollection tags, BasicAuthClient client, string municipality)
		{
			var changeSetId = await client.CreateChangeset(tags);
			Log.LogDebug($"Creating ChangeSet {changeSetId}");
			FileSerializer.WriteXml($"{municipality}/Uploaded/{changeSetId}-Conflated.osc", part);
			var diffResult = await client.UploadChangeset(changeSetId, part);
			//FileSerializer.WriteXml($"{municipality}/Uploaded/{changeSetId}-DiffResult.diff", diffResult);
			await client.CloseChangeset(changeSetId);
			Log.LogDebug($"Closing ChangeSet {changeSetId}");
			return changeSetId;
		}
	}
}