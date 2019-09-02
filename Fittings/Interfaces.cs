using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace OsmPipeline.Fittings
{
	//ISubSystem
	//IBuffer
	//ICache
	//ILogger
	//IMerger
	//ISplitter

	public interface ISource<T> : IEnumerable<T>
	{

	}

	public interface IFilter<T> : IDestination<T>, ISource<T>
	{

	}

	public interface IFixer<TFrom, TTo> : IDestination<TFrom>, ISource<TTo>
	{

	}

	public interface ISplitter<T> : IDestination<T>, ISource<T>
	{

	}

	public interface IMixer<T>
	{

	}

	public interface IDestination<T>
	{
		void Put(T item);
	}

	public class Pump<T>
	{
		private ISource<T> Source;
		private IDestination<T> Destination;
		private Func<T, bool> Filter;

		public Pump(ISource<T> source, IDestination<T> destination, Func<T, bool> filter = null)
		{
			Source = source;
			Destination = destination;
			Filter = filter;
		}

		public void Go()
		{
			foreach (T item in Filter == null ? Source : Source.Where(Filter))
			{
				Destination.Put(item);
			}
		}
	}
}
