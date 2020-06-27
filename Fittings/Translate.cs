using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using OsmSharp.Db;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Translate
	{
		public static OsmGeoResult AsDiffResult(this OsmGeo element, long? newId = null, long? newVersion = null)
		{
			if (newId == null && element.Id <= 0)
				throw new Exception("Invalid ID");

			var result = element.Type == OsmGeoType.Node
				? new NodeResult()
				: element.Type == OsmGeoType.Way
					? (OsmGeoResult)new WayResult()
					: new RelationResult();

			result.OldId = element.Id;
			result.NewId = newId ?? element.Id;
			result.NewVersion = newVersion ?? element.Version + 1;

			return result;
		}

		public static long? GetHighestChangeSetId(this Osm osm)
		{
			return osm.GetElements().Max(e => e.ChangeSetId);
		}

		public static IEnumerable<OsmGeo> GetElements(this Osm osm)
		{
			return new OsmGeo[][] { osm.Nodes, osm.Ways, osm.Relations }
				.Where(a => a != null)
				.SelectMany(a => a);
		}

		public static IEnumerable<OsmGeo> GetElements(this OsmChange change)
		{
			return new OsmGeo[][] { change.Create, change.Modify, change.Delete }
				.Where(a => a != null)
				.SelectMany(a => a);
		}

		public static Osm AsOsm(this IEnumerable<OsmGeo> elements, string generator = null, double? version = .6)
		{
			return new Osm()
			{
				Nodes = elements.OfType<Node>().ToArray(),
				Ways = elements.OfType<Way>().ToArray(),
				Relations = elements.OfType<Relation>().ToArray(),
				Version = version,
				Generator = generator,
				Bounds = elements.OfType<Node>().AsBounds().ExpandBy(15)
			};
		}

		public static Osm Merge(this IEnumerable<Osm> osms, double boundsBufferMeters = 0)
		{
			var bufferLat = boundsBufferMeters == 0 ? 0f : (float)Geometry.DistanceLat(boundsBufferMeters);
			var approxLat = osms.First().Bounds.MaxLatitude.Value;
			var bufferLon = boundsBufferMeters == 0 ? 0f : (float)Geometry.DistanceLon(boundsBufferMeters, approxLat);

			return new Osm()
			{
				Nodes = osms.SelectMany(o => o.Nodes ?? new Node[0]).DistinctBy(n => n.Id).ToArray(),
				Ways = osms.SelectMany(o => o.Ways ?? new Way[0]).DistinctBy(n => n.Id).ToArray(),
				Relations = osms.SelectMany(o => o.Relations ?? new Relation[0]).DistinctBy(n => n.Id).ToArray(),
				Version = osms.First().Version,
				Generator = osms.First().Generator,
				Bounds = new Bounds()
				{
					MaxLatitude = osms.Max(o => o.Bounds.MaxLatitude) + bufferLat,
					MinLatitude = osms.Min(o => o.Bounds.MinLatitude) - bufferLat,
					MaxLongitude = osms.Max(o => o.Bounds.MaxLongitude) + bufferLon,
					MinLongitude = osms.Min(o => o.Bounds.MinLongitude) - bufferLon
				}
			};
		}

		public static IEnumerable<T> DistinctBy<T, K>(this IEnumerable<T> elements, Func<T, K> id)
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

		public static Dictionary<K, V[]> ToDictionary<K, V>(this IEnumerable<IGrouping<K, V>> groups, IEqualityComparer<K> equalityComparer = null)
		{
			return groups.ToDictionary(g => g.Key, g => g.ToArray(), equalityComparer);
		}

		public static Dictionary<K, int> GroupCount<T, K>(this IEnumerable<T> elements, Func<T, K> selector)
		{
			return elements.GroupBy(selector).ToDictionary(g => g.Key, g => g.Count());
		}

		public static Dictionary<T, int> GroupCount<T>(this IEnumerable<T> elements)
		{
			return elements.GroupBy(a => a).ToDictionary(g => g.Key, g => g.Count());
		}

		public static Dictionary<K, V> ToDictionary<K, V>(this IEnumerable<KeyValuePair<K, V>> elements, IEqualityComparer<K> equalityComparer = null)
		{
			return elements.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, equalityComparer);
		}

		public static IEnumerable<T> SelectMany<T>(this IEnumerable<IEnumerable<T>> elements)
		{
			return elements.SelectMany(a => a);
		}
	}
}
