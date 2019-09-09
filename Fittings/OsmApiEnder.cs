using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using OsmSharp.Changesets;
using OsmSharp.IO.API;
using OsmSharp.Tags;

namespace OsmPipeline.Fittings
{
	public class OsmApiEnder
	{
		private readonly AuthClient Client;
		private readonly TagsCollection Tags; // should be ...base

		public OsmApiEnder(ILogger logger, string baseAddress, string userName, string password, TagsCollection tags)
		{
			var factory = new ClientsFactory(logger, new HttpClient(), baseAddress);
			Client = factory.CreateBasicAuthClient(userName, password);
			Tags = tags;
		}

		public void Put(OsmChange item)
		{
			var changesetId = Client.CreateChangeset(Tags).Result;
			var diffResult = Client.UploadChangeset(changesetId, item).Result;
			Client.CloseChangeset(changesetId).Wait();
		}
	}
}
