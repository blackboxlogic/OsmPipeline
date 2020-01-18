using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Translate
	{
		public static IEnumerable<OsmGeo> OsmToGeos(this Osm osm)
		{
			return new OsmGeo[][] { osm.Nodes, osm.Ways, osm.Relations }
				.Where(a => a != null)
				.SelectMany(a => a);
		}

		public static OsmChange GeosToChange(IEnumerable<OsmGeo> create, IEnumerable<OsmGeo> modify,
			IEnumerable<OsmGeo> delete, string generator, double? version = .6)
		{
			return new OsmChange()
			{
				Generator = generator,
				Version = version,
				Create = create?.ToArray(),
				Modify = modify?.ToArray(),
				Delete = delete?.ToArray()
			};
		}

		public static Osm AsOsm(this IEnumerable<OsmGeo> geos, double? version = .6, string generator = null)
		{
			return new Osm()
			{
				Nodes = geos.OfType<Node>().ToArray(),
				Ways = geos.OfType<Way>().ToArray(),
				Relations = geos.OfType<Relation>().ToArray(),
				Version = version,
				Generator = generator
			};
		}

		public static Osm Merge(this IEnumerable<Osm> osms)
		{
			return new Osm()
			{
				Nodes = osms.SelectMany(o => o.Nodes).DistinctBy(n => n.Id).ToArray(),
				Ways = osms.SelectMany(o => o.Ways).DistinctBy(n => n.Id).ToArray(),
				Relations = osms.SelectMany(o => o.Relations).DistinctBy(n => n.Id).ToArray(),
				Version = osms.First().Version,
				Generator = osms.First().Generator,
				Bounds = new Bounds()
				{
					MaxLatitude = osms.Max(o => o.Bounds.MaxLatitude),
					MinLatitude = osms.Min(o => o.Bounds.MinLatitude),
					MaxLongitude = osms.Max(o => o.Bounds.MaxLongitude),
					MinLongitude = osms.Min(o => o.Bounds.MinLongitude)
				}
			};
		}

		private static IEnumerable<T> DistinctBy<T, K>(this IEnumerable<T> elements, Func<T, K> id)
		{
			var keySet = new HashSet<K>();
			var valueSet = new List<T>();
			foreach (var element in elements)
			{
				var key = id(element);
				if (!keySet.Contains(key))
				{
					keySet.Add(key);
					valueSet.Add(element);
				}
			}
			return valueSet;
		}
	}
}
