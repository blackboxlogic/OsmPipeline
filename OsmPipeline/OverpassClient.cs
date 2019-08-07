using System;
using System.IO;
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

		public static string PhoneNotCorrectFormat = 
			@"['phone'!~'^\\+1\\-[0-9]{3}\\-[0-9]{3}\\-[0-9]{4}$']";
		public static string NodePhone11DigitStartWith1 =
			@"['phone'~'^([^0-9]*1)([^0-9]*[0-9]){10}[^0-9]*$']";
		public static string Phone10DigitStartsWith207 =
			@"['phone'~'^([^0-9]*2)([^0-9]*0)([^0-9]*7)([^0-9]*[0-9]){7}[^0-9]*$']";

		private const string Url = "http://overpass-api.de/api/interpreter";

		public static string BuildQuery(OsmGeoType geoType, bool includeMeta, OsmGeo location, params string[] filters)
		{
			if (location.Type == OsmGeoType.Node || !location.Id.HasValue) throw new Exception();

			long locationId = location.Id.Value +
				(location.Type == OsmGeoType.Relation ? RelationIdToArea : WayIdToArea);

			string meta = includeMeta ? " meta" : "";

			return geoType + string.Concat(filters)
				+ "(area: " + locationId + ");"
				+ "out" + meta + ";";
		}

		public static async Task<OsmSharp.API.Osm> Fetch(string query)
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
}
