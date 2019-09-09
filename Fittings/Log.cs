using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
namespace OsmPipeline.Fittings
{
	public class Log<T>
	{
		private ILogger Logger;
		Func<T, string> Stringer;
		
		public Log(ILogger logger, Func<T, string> stringer = null)
		{
			Stringer = stringer ?? (a => a?.ToString() ?? "null");
			Logger = logger;
		}

		public IEnumerable<T> LogEach(IEnumerable<T> source)
		{
			foreach (var item in source)
			{
				Logger.LogInformation(Stringer(item));
			}

			return source;
		}

		public T LogItem(T item)
		{
			Logger.LogInformation(Stringer(item));
			return item;
		}

		public IEnumerable<T> LogCount(IEnumerable<T> source)
		{
			Logger.LogInformation($"{source.Count()} elements");

			return source;
		}
	}
}
