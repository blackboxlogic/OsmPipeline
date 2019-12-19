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
			var osmApiClient = new NonAuthClient("https://www.openstreetmap.org/api/", new HttpClient(), null);
			var map = await osmApiClient.GetMap(bounds);
			return map;
		}

		public static async Task<DiffResult> UploadChange(OsmChange change,
			string comment, string source, string scope, bool review_requested)
		{
			var osmApiClient = new BasicAuthClient(new HttpClient(),
				Static.LogFactory.CreateLogger<BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);
			var changeSetTags = new TagsCollection()
			{
				new Tag("comment", comment),
				new Tag("created_by", Static.Config["CreatedBy"]),
				new Tag("bot", "yes"),
				new Tag("source", source),
				new Tag("review_requested", review_requested ? "yes" : "no"),
				new Tag("scope", scope)
			};
			var changeSetId = await osmApiClient.CreateChangeset(changeSetTags);
			var diffResult = await osmApiClient.UploadChangeset(changeSetId, change);
			await osmApiClient.CloseChangeset(changeSetId);
			return diffResult;
		}
	}
}
