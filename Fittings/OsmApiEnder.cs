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
		private readonly IAuthClient Client;
		private readonly TagsCollectionBase Tags;

		public OsmApiEnder(ILogger logger, string baseAddress, string userName, string password, TagsCollectionBase tags)
		{
			var factory = new ClientsFactory(logger, new HttpClient(), baseAddress);
			Client = factory.CreateBasicAuthClient(userName, password);
			Tags = tags;
		}

		public DiffResult Upload(OsmChange item)
		{
			// Won't need this cast after updating OsmApiClient
			var changesetId = Client.CreateChangeset((TagsCollection)Tags).Result;
			var diffResult = Client.UploadChangeset(changesetId, item).Result;
			Client.CloseChangeset(changesetId).Wait();
			return diffResult;
		}
	}
}
