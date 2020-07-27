using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Changesets;

namespace OsmPipeline.Fittings
{
	public static class Changes
	{
		// Shouldn't matter for address imports, but if API generates errors then the elements might need to be re-ordered (nodes -> ways -> relations)
		// Should break up geographically large changes
		// Note that the order changeset are uploaded is important, can't do in parallel
		public static IEnumerable<OsmChange> SplitByCluster(this OsmChange change,
			Dictionary<string, OsmGeo> index,
			int maxPieceSize = 10_000, int clusterRadiusMeters = 643737) // 400 miles
		{
			var clusters = new List<MutableChange>();
			var handled = new HashSet<OsmGeo>();

			Cluster(change.Create, index, clusterRadiusMeters, clusters, handled, c => c.Create);
			Cluster(change.Modify, index, clusterRadiusMeters, clusters, handled, c => c.Modify);
			Cluster(change.Delete, index, clusterRadiusMeters, clusters, handled, c => c.Delete);

			return clusters.Select(cluster => cluster.AsChange(change)).SelectMany(c => c.SplitByCount(maxPieceSize));
		}

		private static void Cluster(OsmGeo[] elements, Dictionary<string, OsmGeo> index,
			int clusterRadiusMeters, List<MutableChange> clusters, HashSet<OsmGeo> handled,
			Func<MutableChange, List<OsmGeo>> sectionPicker)
		{
			if (elements == null) return;
			foreach (var element in elements.OrderByDescending(e => e.Type)) // relations, then ways, then nodes
			{
				if (!handled.Contains(element))
				{
					var completeElement = element.AsComplete(index);
					var cluster = clusters
						.Select(c => new { Distance = Geometry.MaxDistanceMeters(c.Position, completeElement), c })
						.Where(dc => dc.Distance < clusterRadiusMeters)
						.OrderBy(c => c.Distance)
						.Select(kvp => kvp.c)
						.FirstOrDefault();
					if (cluster == null)
					{
						cluster = new MutableChange() { Position = completeElement.AsPosition() };
						clusters.Add(cluster);
					}

					sectionPicker(cluster).Add(element);
					handled.Add(element);

					if (element is Way way)
					{
						var children = elements.Where(e => e is Node && way.Nodes.Contains(e.Id.Value));

						foreach (var child in children.Except(handled))
						{
							sectionPicker(cluster).Add(child);
							handled.Add(child);
						}
					}
					else if (element is Relation) throw new NotImplementedException("Can't handle relations.");
				}
			}
		}

		private class MutableChange
		{
			public Coordinate Position;
			public readonly List<OsmGeo> Create = new List<OsmGeo>();
			public readonly List<OsmGeo> Modify = new List<OsmGeo>();
			public readonly List<OsmGeo> Delete = new List<OsmGeo>();

			public OsmChange AsChange(OsmChange template)
			{
				return template.EmptyClone(Create.ToArray(), Modify.ToArray(), Delete.ToArray());
			}
		}

		public static IEnumerable<OsmChange> SplitByCount(this OsmChange change, int maxPieceSize = 10_000)
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

		private static OsmChange EmptyClone(this OsmChange change, OsmGeo[] create = null, OsmGeo[] modify = null, OsmGeo[] delete = null)
		{
			return new OsmChange()
			{
				Attribution = change.Attribution,
				Copyright = change.Copyright,
				Generator = change.Generator,
				License = change.License,
				Version = change.Version,
				Create = create,
				Modify = modify,
				Delete = delete
			};
		}

		private static IEnumerable<T[]> AsChunks<T>(this IEnumerable<T> elements, int chunkSize)
		{
			for (int chunk = 0; chunk * chunkSize < elements.Count(); chunk++)
			{
				yield return elements.Skip(chunk * chunkSize).Take(chunkSize).ToArray();
			}
		}

		public static OsmChange FromGeos(IEnumerable<OsmGeo> create, IEnumerable<OsmGeo> modify,
			IEnumerable<OsmGeo> delete, string generator = "OsmPipeline", double? version = .6)
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
	}
}
