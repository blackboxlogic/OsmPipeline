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

		public static OsmChange GeosToChange(IEnumerable<OsmGeo> create, IEnumerable<OsmGeo> modify,
			IEnumerable<OsmGeo> delete, string generator = null, double? version = .6)
		{
			return new OsmChange()
			{
				Generator = generator,
				Version = version,
				Create = create?.ToArray(),
				Modify = modify?.ToArray(),
				Delete = delete?.ToArray(),
				// License, Attribution, and Copyright are not used by the API.
			};
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

		// Shouldn't matter for address import, but if API generates errors then the elements might need to be re-ordered (nodes -> ways -> relations)
		public static IEnumerable<OsmChange> Split(this OsmChange change, int maxPieceSize = 10_000)
		{
			if (change.GetElements().Count() <= maxPieceSize)
			{
				yield return change;
				yield break;
			}
			if (change.Create.Any())
			{
				foreach (var chunk in change.Create.AsChunks(maxPieceSize))
				{
					var clone = change.EmptyClone();
					clone.Create = chunk;
					yield return clone;
				}
			}
			if (change.Modify.Any())
			{
				foreach (var chunk in change.Modify.AsChunks(maxPieceSize))
				{
					var clone = change.EmptyClone();
					clone.Modify = chunk;
					yield return clone;
				}
			}
			if (change.Delete.Any())
			{
				foreach (var chunk in change.Delete.AsChunks(maxPieceSize))
				{
					var clone = change.EmptyClone();
					clone.Delete = chunk;
					yield return clone;
				}
			}
		}

		private static OsmChange EmptyClone(this OsmChange change)
		{
			return new OsmChange()
			{
				Attribution = change.Attribution,
				Copyright = change.Copyright,
				Generator = change.Generator,
				License = change.License,
				Version = change.Version
			};
		}

		private static IEnumerable<T[]> AsChunks<T>(this IEnumerable<T> elements, int chunkSize)
		{
			for (int chunk = 0; chunk * chunkSize < elements.Count(); chunk++)
			{
				yield return elements.Skip(chunk * chunkSize).Take(chunkSize).ToArray();
			}
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
