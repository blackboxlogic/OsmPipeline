﻿using BAMCIS.GeoJSON;
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
		//https://gis.maine.gov/arcgis/rest/services/Location/Maine_E911_Addresses_Roads_PSAP/MapServer/1?f=pjson
		//maxRecordCount
		private const int MaxRecords = 5000;
		//"currentVersion": 10.4,
		//"cimVersion": "1.2.0",

		// gets dictionary MunicipalityName -> Number of addresses
		public static async Task<Dictionary<string, Municipality>> GetMunicipalities()
		{
			var stats = new[] { new OutStatistics() { statisticType = "count", onStatisticField = "TOWN", outStatisticFieldName = "mcount" } };

			var json = await FetchOnce(0, "1=1", "upper(TOWN)", stats); // GIS responds with json even if f=geojson because groupby
			var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<GroupByJson>(json);
			var municipalities = deserialized.Features.ToDictionary(
				f => (string)f.Attributes["Expr1"],
				f => (long)f.Attributes["mcount"]);
			return municipalities.ToDictionary(kvp => kvp.Key, kvp => new Municipality() { AddressCount = kvp.Value, Name = kvp.Key });
		}

		public class Municipality
		{
			public string Name;
			public long AddressCount;
			public DateTime? ImportDate;
			public string Notes;
			public List<long> ChangeSetIds = new List<long>();
			public List<long> BlackList = new List<long>();
			public List<long> WhiteList = new List<long>();
			public List<string> BlackTags = new List<string>(); // like: [ "12345.name", "12345.building" ]
			public List<long> IgnoreList = new List<long>();

			public override string ToString() => Name;
		}

		public static async Task<FeatureCollection> FetchMunicipality(string town, int? limit = null)
		{
			return await FetchMany($"upper(TOWN)=upper('{town}')", limit);
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
				FeatureCollection collection = FeatureCollection.FromJson(json); // can throw Newtonsoft.Json.JsonReaderException
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
