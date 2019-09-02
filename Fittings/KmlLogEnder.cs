using System;
using Microsoft.Extensions.Logging;
using SharpKml.Dom;
namespace OsmPipeline.Fittings
{
	public class KmlLoggingEnder : IDestination<Element>
	{
		private ILogger Logger;

		public KmlLoggingEnder(ILogger logger)
		{
			Logger = logger;
		}

		public void Put(Element item)
		{
			Logger.LogInformation(item.ToString());
		}
	}
}
