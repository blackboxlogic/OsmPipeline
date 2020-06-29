using System;
using System.Collections.Generic;
using System.Linq;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Complete;

namespace OsmPipeline.Fittings
{
	public static class BoundsExtentions
	{
		public static Bounds ExpandBy(this Bounds bounds, double bufferMeters)
		{
			if (bufferMeters == 0 || bounds == null) return bounds;
			var bufferLat = (float)Geometry.DistanceLat(bufferMeters);
			var approxLat = (bounds.MinLatitude.Value + bounds.MaxLatitude.Value) / 2;
			var bufferLon = (float)Geometry.DistanceLon(bufferMeters, approxLat);

			return new Bounds()
			{
				MaxLatitude = bounds.MaxLatitude + bufferLat,
				MinLatitude = bounds.MinLatitude - bufferLat,
				MaxLongitude = bounds.MaxLongitude + bufferLon,
				MinLongitude = bounds.MinLongitude - bufferLon
			};
		}

		public static IEnumerable<KeyValuePair<Bounds, CompleteOsmGeo[]>> SliceRecusive(this CompleteWay[] elements, int maxElementsPerSlice = 1000, Bounds bounds = null)
		{
			bounds = bounds ?? elements.AsBounds();
			if (elements.Length <= maxElementsPerSlice)
			{
				yield return new KeyValuePair<Bounds, CompleteOsmGeo[]>(bounds, elements);
				yield break;
			}

			var halfs = bounds.Half();
			var inFirstHalf = elements.Where(e => IsTouching(halfs[0], e)).ToArray();
			var groups = new[] { inFirstHalf, elements.Except(inFirstHalf).ToArray() };

			for (int i = 0; i < halfs.Length; i++)
			{
				foreach (var subGroup in groups[i].SliceRecusive(maxElementsPerSlice, halfs[i]).ToArray())
				{
					if (subGroup.Value.Any())
						yield return subGroup;
				}
			}
		}

		public static bool IsTouching(Bounds bounds, ICompleteOsmGeo geo)
		{
			switch (geo)
			{
				case Node node:
					return IsTouching(bounds, node);
				case CompleteWay way:
					return IsTouching(bounds, way);
				case CompleteRelation relation:
					return IsTouching(bounds, relation);
				default:
					throw new Exception("Unknown Complete Element Type.");
			}
		}

		public static bool IsTouching(Bounds bounds, CompleteRelation relation)
		{
			return relation.Members.Any(m => IsTouching(bounds, m.Member));
		}

		public static bool IsTouching(Bounds bounds, CompleteWay way)
		{
			return way.Nodes.Any(n => IsTouching(bounds, n));
		}

		public static bool IsTouching(Bounds bounds, Node node)
		{
			return bounds.MaxLatitude >= node.Latitude
				&& bounds.MinLatitude <= node.Latitude
				&& bounds.MaxLongitude >= node.Longitude
				&& bounds.MinLongitude <= node.Longitude;
		}

		public static Bounds[] Half(this Bounds bounds)
		{
			var halfLat = (bounds.MaxLatitude + bounds.MinLatitude) / 2;
			var halfLon = (bounds.MaxLongitude + bounds.MinLongitude) / 2;

			if (bounds.MaxLatitude - bounds.MinLatitude > bounds.MaxLongitude - bounds.MinLongitude)
			{
				return new[] {new Bounds()
					{
						MaxLatitude = bounds.MaxLatitude,
						MaxLongitude = bounds.MaxLongitude,
						MinLatitude = halfLat,
						MinLongitude = bounds.MinLongitude
					},
					new Bounds()
					{
						MaxLatitude = halfLat,
						MaxLongitude = bounds.MaxLongitude,
						MinLatitude = bounds.MinLatitude,
						MinLongitude = bounds.MinLongitude
					} };
			}
			else
			{
				return new[]{
					new Bounds()
					{
						MaxLatitude = bounds.MaxLatitude,
						MaxLongitude = halfLon,
						MinLatitude = bounds.MinLatitude,
						MinLongitude = bounds.MinLongitude
					},
					new Bounds()
					{
						MaxLatitude = bounds.MaxLatitude,
						MaxLongitude = bounds.MaxLongitude,
						MinLatitude = bounds.MinLatitude,
						MinLongitude = halfLon
					} };
			}
		}

		public static Bounds[] Quarter(this Bounds bounds)
		{
			var halfLat = (bounds.MaxLatitude + bounds.MinLatitude) / 2;
			var halfLon = (bounds.MaxLongitude + bounds.MinLongitude) / 2;
			return new[] {new Bounds()
			{
				MaxLatitude = bounds.MaxLatitude,
				MaxLongitude = bounds.MaxLongitude,
				MinLatitude = halfLat,
				MinLongitude = halfLon
			},
			new Bounds()
			{
				MaxLatitude = halfLat,
				MaxLongitude = halfLon,
				MinLatitude = bounds.MinLatitude,
				MinLongitude = bounds.MinLongitude
			},
			new Bounds()
			{
				MaxLatitude = bounds.MaxLatitude,
				MaxLongitude = halfLon,
				MinLatitude = halfLat,
				MinLongitude = bounds.MinLongitude
			},
			new Bounds()
			{
				MaxLatitude = halfLat,
				MaxLongitude = bounds.MaxLongitude,
				MinLatitude = bounds.MinLatitude,
				MinLongitude = halfLon
			} };
		}

		public static IEnumerable<Node> FastNodesInBounds(this Bounds bounds, SortedList<double, Node[]> byLat, SortedList<double, Node[]> byLon)
		{
			var subLat = SeekIndexRange(byLat, bounds.MinLatitude.Value, bounds.MaxLatitude.Value);
			var subLon = SeekIndexRange(byLon, bounds.MinLongitude.Value, bounds.MaxLongitude.Value);
			return subLat.Intersect(subLon);
		}

		private static IEnumerable<Node> SeekIndexRange(SortedList<double, Node[]> list, float minKey, float maxKey)
		{
			int minIndex = 0;
			int maxIndex = list.Keys.Count;
			int mid = (maxIndex + minIndex) / 2;

			while (mid != minIndex && (list.Keys[mid] < minKey || list.Keys[mid - 1] >= minKey))
			{
				if (list.Keys[mid] < minKey)
				{
					minIndex = mid;
					mid = (maxIndex + minIndex) / 2;
				}
				else if (list.Keys[mid - 1] >= minKey)
				{
					maxIndex = mid;
					mid = (maxIndex + minIndex) / 2;
				}
				else
				{
					mid--;
				}
			}

			minIndex = mid;
			int count = list.Keys.Skip(mid).TakeWhile(e => e <= maxKey).Count();
			var subValues = list.Values.Skip(mid).Take(count).SelectMany(n => n);
			return subValues;
		}

		public static Bounds AsBounds(this IEnumerable<ICompleteOsmGeo> elements)
		{
			var allBounds = elements.Select(e => e.AsBounds()).ToArray();
			if (!allBounds.Any()) return null;
			return new Bounds()
			{
				MaxLatitude = allBounds.Max(n => n.MaxLatitude),
				MinLatitude = allBounds.Min(n => n.MinLatitude),
				MaxLongitude = allBounds.Max(n => n.MaxLongitude),
				MinLongitude = allBounds.Min(n => n.MinLongitude)
			};
		}

		public static Bounds AsBounds(this ICompleteOsmGeo element)
		{
			if (element is Node node)
			{
				return new Bounds()
				{
					MaxLatitude = (float)node.Latitude,
					MinLatitude = (float)node.Latitude,
					MaxLongitude = (float)node.Longitude,
					MinLongitude = (float)node.Longitude
				};
			}
			else if (element is CompleteWay way)
			{
				return new Bounds()
				{
					MaxLatitude = (float)way.Nodes.Max(n => n.Latitude),
					MinLatitude = (float)way.Nodes.Min(n => n.Latitude),
					MaxLongitude = (float)way.Nodes.Max(n => n.Longitude),
					MinLongitude = (float)way.Nodes.Min(n => n.Longitude)
				};
			}
			else if (element is CompleteRelation relation)
			{
				var allBounds = relation.Members.Select(m => AsBounds(m.Member));
				return new Bounds()
				{
					MaxLatitude = (float)allBounds.Max(n => n.MaxLatitude),
					MinLatitude = (float)allBounds.Min(n => n.MinLatitude),
					MaxLongitude = (float)allBounds.Max(n => n.MaxLongitude),
					MinLongitude = (float)allBounds.Min(n => n.MinLongitude)
				};
			}
			throw new Exception("element wasn't a node, way or relation");
		}
	}
}
