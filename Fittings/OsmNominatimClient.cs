using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace OsmPipeline.Fittings
{
	/// <summary>
	/// A Client for <see href="https://wiki.openstreetmap.org/wiki/Nominatim">OSM's Nominatim</see>.
	/// Please read the <see href="https://operations.osmfoundation.org/policies/nominatim/">Terms of Service</see>.
	/// </summary>
	public class OsmNominatimClient
	{
		public readonly string BaseAddress;
		private readonly string ContactEmail;
		private readonly ProductInfoHeaderValue UserAgent;
		private readonly HttpClient HttpClient;
		private readonly ILogger Logger;
		private static DateTime NextRequest = DateTime.UtcNow;
		private static readonly object ThrottleLock = new object();

		/// <param name="productName">Included as the userAgent to conform to ToS.</param>
		/// <param name="baseAddress">The base url for the api, omit the trailing slash.</param>
		/// <param name="contactEmail">Included in requests to conform to Tos.</param>
		public OsmNominatimClient(string productName,
			string baseAddress = "https://nominatim.openstreetmap.org",
			string contactEmail = null,
			HttpClient httpClient = null,
			ILoggerFactory loggerFactory = null)
		{
			UserAgent = new ProductInfoHeaderValue(new ProductHeaderValue(productName));
			ContactEmail = contactEmail;
			BaseAddress = baseAddress;
			HttpClient = httpClient ?? new HttpClient();
			Logger = loggerFactory?.CreateLogger<OsmNominatimClient>();
		}

		public async Task<Place[]> LookupNode(long id) => await Lookup("N" + id);

		public async Task<Place[]> LookupWay(long id) => await Lookup("W" + id);

		public async Task<Place[]> LookupRelation(long id) => await Lookup("R" + id);

		public async Task<Place[]> Lookup(params string[] ids)
		{
			return await Get<Place[]>($"lookup?osm_ids={string.Join(",", ids)}");
		}

		public async Task<Place[]> Search(string query, int limit = 1)
		{
			return await Get<Place[]>($"search/{HttpUtility.UrlEncode(query)}?addressdetails=1&limit={limit}");
		}

		public async Task<ReverseGeocode> ReverseNode(long id) => await Reverse('N', id);

		public async Task<ReverseGeocode> ReverseWay(long id) => await Reverse('W', id);

		public async Task<ReverseGeocode> ReverseRelation(long id) => await Reverse('R', id);

		private async Task<ReverseGeocode> Reverse(char osmType, long id)
		{
			return await Get<ReverseGeocode>($"reverse?osm_type={osmType}&osm_id={id}");
		}

		public async Task<ReverseGeocode> Reverse(double lat, double lon)
		{
			return await Get<ReverseGeocode>($"reverse?lat={ToString(lat)}&lon={ToString(lon)}");
		}

		// prevent scientific notation
		private string ToString(double number)
		{
			return number.ToString("0.########");
		}

		protected async Task<T> Get<T>(string action)
		{
			var address = $"{BaseAddress}/{action}&format=jsonv2";
			if (ContactEmail != null)
			{
				address += "&email=" + HttpUtility.UrlEncode(ContactEmail);
			}

			using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, address))
			{
				request.Headers.UserAgent.Add(UserAgent);
				Logger?.LogInformation($"GET: {address}");

				lock (ThrottleLock)
				{
					var wait = NextRequest - DateTime.UtcNow;
					if (wait.Ticks > 0) Thread.Sleep(wait);
					NextRequest = DateTime.UtcNow.AddSeconds(1);
				}

				var response = await HttpClient.SendAsync(request);
				await VerifyAndLogReponse(response);
				var body = await response.Content.ReadAsStringAsync();
				var element = JsonConvert.DeserializeObject<T>(body);

				return element;
			}
		}

		protected async Task VerifyAndLogReponse(HttpResponseMessage response)
		{
			if (!response.IsSuccessStatusCode)
			{
				var message = await response.Content.ReadAsStringAsync();
				message = $"Request failed: {response.StatusCode}-{response.ReasonPhrase} {message}";
				Logger?.LogError(message);
				throw new Exception(response.RequestMessage?.RequestUri + Environment.NewLine
					+ response.StatusCode + " " + message);
			}
			else
			{
				var message = $"Request succeeded: {response.StatusCode}-{response.ReasonPhrase}";
				Logger?.LogInformation(message);
				var headers = string.Join(", ", response.Content.Headers.Select(h => $"{h.Key}: {string.Join(";", h.Value)}"));
				Logger?.LogDebug(headers);
			}
		}
	}

	public class ReverseGeocode : Place
	{
		public string addresstype;
		public string name;
	}

	public class Place
	{
		public long place_id;
		public string license;
		public string osm_type;
		public long? osm_id;
		/// <summary>
		/// [ lat min, lat max, lon min, lon max ]
		/// </summary>
		public float[] boundingbox;
		public double? lat; // of centroid
		public double? lon; // of centroid
		public string display_name; // comma separated list
		public string category;
		public string type;
		public double? importance;
		public string icon;
		public Address address;
		public ExtraTags extratags;
		public int? place_rank;
	}

	public class Address
	{
		// unit?
		public string house_number;
		public string road;
		public string city;
		public string county;
		public string state;
		public string postcode;
		public string country;
		public string country_code;
	}

	public class ExtraTags
	{
		public string capital;
		public string website;
		public string wikidata;
		public string wikipedia;
		public string population;
	}
}
