using BAMCIS.GeoJSON;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Linq;
using System.IO;

namespace OsmPipeline.Fittings
{
	public static class CachedGeoJsonAPISource
	{
		public static FeatureCollection FromFileOrFetch(string municipality)
		{
			string filename = municipality + "Addresses.GeoJson";
			if (File.Exists(filename))
			{
				var json = File.ReadAllText(filename);
				return FeatureCollection.FromJson(json);
			}

			var collection = FetchMany(municipality);
			File.WriteAllText(filename, collection.ToJson());

			return collection;
		}

		public static FeatureCollection FetchMany(string municipality, int? limit = null)
		{
			List<Feature> fullSet = new List<Feature>();

			while(true)
			{
				int before = fullSet.Count;
				string json = FetchOnce(fullSet.Count, municipality);
				FeatureCollection collection = FeatureCollection.FromJson(json);
				fullSet.AddRange(collection.Features);
				int added = fullSet.Count - before;
				if (added < 5000 || fullSet.Count >= limit) break;
			}

			return new FeatureCollection(fullSet);
		}

		private static string FetchOnce(int offset, string municipality)
		{
			// Using HttpUtility to generate the parameters for the query string handles HTTP escaping the values.
			// Change these:
			var parameters = HttpUtility.ParseQueryString(string.Empty);
			parameters["where"] = $"MUNICIPALITY='{municipality}'";
			parameters["resultOffset"] = offset.ToString();
			parameters["outFields"] = "*";
			parameters["f"] = "geojson";

			//
			var address = @"https://gis.maine.gov/arcgis/rest/services/Location/Maine_E911_Addresses_Roads_PSAP/MapServer/1/query?"
				+ parameters;

			// Disposing HttpClient is not best practice. It's good enough and I'm keeping this simple.
			using (var client = new HttpClient())
			{
				using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, address))
				{
					// .Result from an Async function is not best practice. It's good enough and I'm keeping this simple.
					var response = client.SendAsync(request).Result;
					var content = response.Content.ReadAsStringAsync().Result;
					if (!response.IsSuccessStatusCode)
					{
						throw new Exception($"{content}: {response.StatusCode}");
					}
					return content; // the content of the response
				}
			}
		}
	}
}
