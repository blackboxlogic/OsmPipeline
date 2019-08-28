using System;
using Microsoft.Extensions.Logging;

namespace OsmPipeline.Fittings
{
	public class LoggingEnder<T> : IDestination<T>
	{
		private ILogger Logger;

		public LoggingEnder(ILogger logger)
		{
			Logger = logger;
		}

		public void Put(T item)
		{
			Logger.LogInformation(item.ToString());
		}
	}
}
