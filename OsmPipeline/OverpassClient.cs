using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace OsmPipeline
{
	public static class OverpassAPI
	{
		// Day Limit:10,000 queries, 5 gb
		private const string Url = "http://overpass-api.de/api/interpreter";
		private static XmlSerializer Deserializer = new XmlSerializer(typeof(osmEntity));

		public static async Task<osmEntity> Fetch(string query)
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
			request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Pipeline-OverpassClient", "0.1"));
			var response = await client.SendAsync(request);

			if (!response.IsSuccessStatusCode)
			{
				throw new Exception(response.StatusCode + "/" + response.ReasonPhrase);
			}

			var responseStream = await response.Content.ReadAsStreamAsync();
			//Console.WriteLine(new System.IO.StreamReader(responseStream).ReadToEnd());
			var osm = (osmEntity)Deserializer.Deserialize(responseStream);

			return osm;
		}
	}
}
