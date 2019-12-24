using BAMCIS.GeoJSON;
using OsmSharp.API;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace OsmPipeline.Fittings
{
	public static class GeoJsonAPISource
	{
		private const int MaxRecords = 5000;

		// gets dictionary MunicipalityName -> Number of addresses
		public static async Task<Dictionary<string, Municipality>> GetMunicipalities()
		{
			var stats = new[] { new OutStatistics() { statisticType = "count", onStatisticField = "MUNICIPALITY", outStatisticFieldName = "mcount" } };

			var json = await FetchOnce(0, "1=1", "MUNICIPALITY", stats); // GIS responds with json even if f=geojson because groupby
			var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<GroupByJson>(json);
			var municipalities = deserialized.Features.ToDictionary(
				f => (string)f.Attributes["MUNICIPALITY"],
				f => (long)f.Attributes["mcount"]);
			return municipalities.ToDictionary(kvp => kvp.Key, kvp => new Municipality() { AddressCount = kvp.Value });
		}

		public class Municipality
		{
			public long AddressCount;
			public DateTime? ImportDate;
			public string Notes;
			public long[] ChangeSetIds;
		}

		public static async Task<FeatureCollection> FetchMunicipality(string municipality, int? limit = null)
		{
			return await FetchMany($"MUNICIPALITY='{municipality}'", limit);
		}

		public static async Task<FeatureCollection> Fetch(Bounds bounds, int? limit = null)
		{
			// Could also use https://gis.maine.gov/arcgis/sdk/rest/index.html#/Query_Feature_Service_Layer/02ss0000002r000000/
			var where = $"Latitude>={bounds.MinLatitude} and Latitude<={bounds.MaxLatitude} and Longitude>={bounds.MinLongitude} and Longitude<={bounds.MaxLongitude}";
			return await FetchMany(where, limit);
		}

		private static async Task<FeatureCollection> FetchMany(string where = "1=1", int? limit = null)
		{
			List<Feature> fullSet = new List<Feature>();

			while(true)
			{
				int before = fullSet.Count;
				string json = await FetchOnce(fullSet.Count, where);
				FeatureCollection collection = FeatureCollection.FromJson(json);
				fullSet.AddRange(collection.Features);
				int added = fullSet.Count - before;
				if (added < MaxRecords || fullSet.Count >= limit) break;
			}

			return new FeatureCollection(fullSet);
		}

		private static async Task<string> FetchOnce(int offset, string where = "1=1", string groupBy = "", OutStatistics[] stats = null)
		{
			var parameters = HttpUtility.ParseQueryString(string.Empty);
			parameters["where"] = where;
			parameters["resultOffset"] = offset.ToString();
			parameters["outFields"] = "*";
			parameters["f"] = "geojson";
			parameters["groupByFieldsForStatistics"] = groupBy;
			parameters["outStatistics"] = Newtonsoft.Json.JsonConvert.SerializeObject(stats);

			// Should be config.
			var address = @"https://gis.maine.gov/arcgis/rest/services/Location/Maine_E911_Addresses_Roads_PSAP/MapServer/1/query?"
				+ parameters;

			using (var client = new HttpClient())
			{
				using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, address))
				{
					var response = await client.SendAsync(request);
					var content = await response.Content.ReadAsStringAsync();
					if (!response.IsSuccessStatusCode)
					{
						throw new Exception($"{content}: {response.StatusCode}");
					}
					return content; // the content of the response
				}
			}
		}

		class OutStatistics
		{
			public string statisticType; // min, max, avg, count
			public string onStatisticField;
			public string outStatisticFieldName;
		}

		class GroupByJson
		{
			public JsonFeature[] Features;
		}

		class JsonFeature
		{
			public Dictionary<string, object> Attributes;
		}
	}
}
