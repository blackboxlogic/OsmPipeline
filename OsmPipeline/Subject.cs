using OsmSharp.API;
using OsmSharp;
using System.Threading.Tasks;
using OsmPipeline.Fittings;
using System.Net.Http;
using OsmSharp.IO.API;
using OsmSharp.Changesets;
using OsmSharp.Tags;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace OsmPipeline
{
	public static class Subject
	{
		private static ILogger Log;

		static Subject()
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
			var osmApiClient = new NonAuthClient(Static.Config["OsmApiUrl"], new HttpClient(),
				Static.LogFactory.CreateLogger<NonAuthClient>());
			var map = await osmApiClient.GetMap(bounds);
			return map;
		}

		public static TagsCollection GetCommitTags(string municipality)
		{
			return new TagsCollection()
			{
				new Tag("comment", Static.Config["CommitComment"]),
				new Tag("created_by", "OsmPipeline"),
				new Tag("created_by_library", Static.Config["created_by_library"]),
				new Tag("municipality", municipality),
				new Tag("osm_wiki_documentation_page", Static.Config["osm_wiki_documentation_page"]),
				new Tag("bot", "yes"),
				new Tag("source", Static.Config["DataSourceName"])
			};
		}

		public static async Task<long> UploadChange(OsmChange change, string municipality)
		{
			Log.LogInformation("Uploading change to OSM");
			var changeTags = GetCommitTags(municipality);
			var osmApiClient = new BasicAuthClient(new HttpClient(),
				Static.LogFactory.CreateLogger<BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);
			var changeSetId = await osmApiClient.CreateChangeset(changeTags);
			var diffResult = await osmApiClient.UploadChangeset(changeSetId, change);
			await osmApiClient.CloseChangeset(changeSetId);
			return changeSetId;
		}
	}
}
