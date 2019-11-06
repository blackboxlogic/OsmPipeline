﻿using BAMCIS.GeoJSON;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace OsmPipeline.Fittings
{
	public static class GeoJsonAPISource
	{
		public static async Task<FeatureCollection> FetchMany(string municipality, int? limit = null)
		{
			List<Feature> fullSet = new List<Feature>();

			while(true)
			{
				int before = fullSet.Count;
				string json = await FetchOnce(fullSet.Count, municipality);
				FeatureCollection collection = FeatureCollection.FromJson(json);
				fullSet.AddRange(collection.Features);
				int added = fullSet.Count - before;
				if (added < 5000 || fullSet.Count >= limit) break;
			}

			return new FeatureCollection(fullSet);
		}

		private static async Task<string> FetchOnce(int offset, string municipality)
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
	}
}