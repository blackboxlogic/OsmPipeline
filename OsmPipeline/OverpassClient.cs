using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using OsmSharp;

namespace OsmPipeline
{
	// Day Limit:10,000 queries, 5 gb
	public static class OverpassAPI
	{
		private const long RelationIdToArea = 3600000000;
		private const long WayIdToArea = 2400000000;

		private const string Url = "http://overpass-api.de/api/interpreter";

		public static string Query(OsmGeoType geoType, OsmGeo location, params Filter[] filters)
		{
			if (location?.Id == null || location.Type == OsmGeoType.Node) throw new Exception("location is invalid.");

			long locationId = location.Id.Value +
				(location.Type == OsmGeoType.Relation ? RelationIdToArea : WayIdToArea);

			return geoType.ToString().ToLower()
				+ string.Concat(filters.Select(f => "\n    " + f))
				+ "\n(area:" + locationId + ");";
		}

		public static string Union(params string[] subqueries)
		{
			return "(" + string.Join("\n", subqueries) + ");";
		}

		public static string AddOut(string query, bool includeMeta = false)
		{
			if (includeMeta)
			{
				return $"{query}\nout meta;";
			}
			else
			{
				return $"{query}\nout;";
			}
		}

		public static async Task<OsmSharp.API.Osm> Execute(string query)
		{
			var serializer = new XmlSerializer(typeof(OsmSharp.API.Osm));
			var stream = await FetchStream(query);
			var res = serializer.Deserialize(stream) as OsmSharp.API.Osm;
			return res;
		}

		public static async Task<string> FetchAsString(string query)
		{
			return new StreamReader(await FetchStream(query)).ReadToEnd();
		}

		private static async Task<Stream> FetchStream(string query)
		{
			query = query.Replace("+", @"%2B");
			//query = query.Replace("/", @"%2F");
			//query = query.Replace("=", @"%3D");
			var client = new HttpClient();
			var content = new StringContent("data=" + query, Encoding.UTF8, "application/x-www-form-urlencoded");

			var request = new HttpRequestMessage(HttpMethod.Post, Url);
			request.Content = content;
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
			request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
			request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OsmPipeline", "0.1"));
			//request.Headers.Referrer = new Uri("https://github.com/blackboxlogic/OsmPipeline");
			var response = await client.SendAsync(request);

			if (!response.IsSuccessStatusCode)
			{
				throw new Exception(response.StatusCode + "/" + response.ReasonPhrase);
			}

			var responseStream = await response.Content.ReadAsStreamAsync();

			return responseStream;
		}
	}

	public class Filter
	{
		public Filter(string key, Comp comp, string value)
		{
			Key = key;
			Comparor = comp;
			Value = value;
		}

		private Dictionary<Comp, string> Comparisons = new Dictionary<Comp, string>() {
				{ Comp.Equal, "="},
				{ Comp.NotEqual, "!="},
				{ Comp.Like, "~"},
				{ Comp.NotLike, "!~"}
			};

		public string Key;
		public Comp Comparor;
		public string Value;

		public override string ToString()
		{
			return $"['{Key}'{Comparisons[Comparor]}'{Value}']";
		}
	}

	public enum Comp
	{
		Equal,
		NotEqual,
		Like,
		NotLike
	}
}
