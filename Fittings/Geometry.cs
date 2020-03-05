using BAMCIS.GeoJSON;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Complete;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Geometry
	{
		public static Bounds ExpandBy(this Bounds bounds, double bufferMeters)
		{
			if (bufferMeters == 0) return bounds;
			var bufferLat = (float)DistanceLat(bufferMeters);
			var approxLat = bounds.MinLatitude.Value;
			var bufferLon = (float)DistanceLon(bufferMeters, approxLat);

			return new Bounds()
			{
				MaxLatitude = bounds.MaxLatitude + bufferLat,
				MinLatitude = bounds.MinLatitude - bufferLat,
				MaxLongitude = bounds.MaxLongitude + bufferLon,
				MinLongitude = bounds.MinLongitude - bufferLon
			};
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

		public static string GetDistanceAndDirectionArrow(Position from, Position to)
		{
			return (int)DistanceMeters(from, to) + "m" + GetDirectionArrow(from, to);
		}

		public static char GetDirectionArrow(Position from, Position to)
		{
			var theta = Math.Atan2(to.Latitude - from.Latitude, to.Longitude - from.Longitude);
			var slice = (int)((theta + Math.PI) / (2 * Math.PI) * 16);
			var arrow = "←↙↙↓↓↘↘→→↗↗↑↑↖↖←"[slice];
			return arrow;
		}

		public static char ReverseArrow(char arrow)
		{
			return "→↗↑↖←↙↓↘"["←↙↓↘→↗↑↖".IndexOf(arrow)];
		}

		// Dictionary is keys on: OsmGeo.Type.ToString() + element.Id
		public static ICompleteOsmGeo AsComplete(this OsmGeo parent, Dictionary<string, OsmGeo> possibleChilden)
		{
			if (parent is ICompleteOsmGeo done) return done;
			else if (parent is Way way)
			{
				return new CompleteWay()
				{
					Id = way.Id.Value,
					Nodes = way.Nodes.Select(n => possibleChilden[OsmGeoType.Node.ToString() + n]).OfType<Node>().ToArray()
				};
			}
			else if (parent is Relation relation)
			{
				var members = relation.Members
						.Where(m => possibleChilden.ContainsKey(m.Type.ToString() + m.Id))
						.Select(m => new CompleteRelationMember()
						{
							Member = AsComplete(possibleChilden[m.Type.ToString() + m.Id], possibleChilden),
							Role = m.Role
						})
						.ToArray();
				return new CompleteRelation()
				{
					Id = relation.Id.Value,
					Members = members
				};
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}

		public static IEnumerable<OsmGeo> WithChildren(this IEnumerable<OsmGeo> parents, Dictionary<string, OsmGeo> possibleChilden)
		{
			return parents.SelectMany(p => p.WithChildren(possibleChilden));
		}

		public static IEnumerable<OsmGeo> WithChildren(this OsmGeo parent, Dictionary<string, OsmGeo> possibleChilden)
		{
			if (parent is Node) return new[] { parent };
			else if (parent is Way way)
			{
				return way.Nodes.Select(n => possibleChilden[OsmGeoType.Node.ToString() + n])
					.Append(parent);
			}
			else if (parent is Relation relation)
			{
				return relation.Members
					.SelectMany(m => WithChildren(possibleChilden[m.Type.ToString() + m.Id], possibleChilden))
					.Append(parent);
			}
			throw new Exception("OsmGeo wasn't a Node, Way or Relation.");
		}

		// Either IN a building, or distanceMeters from a building and no other building within isolationMeters
		public static Dictionary<ICompleteOsmGeo, Node[]> NodesInOrNearCompleteElements(
			ICompleteOsmGeo[] buildings, Node[] nodes, double distanceMeters, double isolationMeters)
		{
			var matchInside = NodesInCompleteElements(buildings, nodes);

			buildings = buildings.Except(matchInside.Keys).ToArray();
			nodes = nodes.Except(matchInside.SelectMany(r => r.Value)).ToArray();
			var isolated = NodesNearCompleteElements(buildings, nodes, isolationMeters);
			nodes = isolated.SelectMany(kvp => kvp.Value).GroupBy(n => n).Select(n => n.ToArray()).Where(n => n.Length == 1).SelectMany(n => n).ToArray();
			var matchNear = NodesNearCompleteElements(buildings, nodes, distanceMeters);

			return matchInside.Concat(matchNear).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}

		public static Dictionary<ICompleteOsmGeo, Node[]> NodesInCompleteElements(ICompleteOsmGeo[] buildings, Node[] nodes)
		{
			var byLat = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Latitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));
			var byLon = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Longitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));

			var result = new Dictionary<ICompleteOsmGeo, Node[]>();
			foreach (var building in buildings)
			{
				var bounds = building.AsBounds().ExpandBy(1); // 1 because we loose precision from double to float
				var candidates = FastNodesInBounds(bounds, byLat, byLon);
				var inners = candidates.Where(n => IsNodeInBuilding(n, building)).ToArray();
				if (inners.Any()) result.Add(building, inners);
			}

			return result;
		}

		// Can return mutliple buildings having the same node near them!
		public static Dictionary<ICompleteOsmGeo, Node[]> NodesNearCompleteElements(
			ICompleteOsmGeo[] elements, Node[] nodes, double withinMeters)
		{
			var byLat = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Latitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));
			var byLon = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Longitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));

			var result = new Dictionary<ICompleteOsmGeo, Node[]>();
			
			foreach (var element in elements)
			{
				var bounds = element.AsBounds().ExpandBy(withinMeters);
				var candidates = FastNodesInBounds(bounds, byLat, byLon);
				var inners = candidates.Where(n => DistanceMeters(element, n) <= withinMeters).ToArray();
				if (inners.Any()) result.Add(element, inners);
			}

			return result;
		}

		private static IEnumerable<Node> FastNodesInBounds(Bounds bounds, SortedList<double, Node[]> byLat, SortedList<double, Node[]> byLon)
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

			while (mid != minIndex && (list.Keys[mid] < minKey || list.Keys[mid -1] >= minKey))
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
			return new Bounds() {
				MaxLatitude = (float)allBounds.Max(n => n.MaxLatitude),
				MinLatitude = (float)allBounds.Min(n => n.MinLatitude),
				MaxLongitude = (float)allBounds.Max(n => n.MaxLongitude),
				MinLongitude = (float)allBounds.Min(n => n.MinLongitude)
			};
		}

		public static Bounds AsBounds(this ICompleteOsmGeo element)
		{
			if (element is Node node)
			{
				return new Bounds() {
					MaxLatitude = (float)node.Latitude,
					MinLatitude = (float)node.Latitude,
					MaxLongitude = (float)node.Longitude,
					MinLongitude = (float)node.Longitude
				};
			}
			else if (element is CompleteWay way)
			{
				return new Bounds() {
					MaxLatitude = (float)way.Nodes.Max(n => n.Latitude),
					MinLatitude = (float)way.Nodes.Min(n => n.Latitude),
					MaxLongitude = (float)way.Nodes.Max(n => n.Longitude),
					MinLongitude = (float)way.Nodes.Min(n => n.Longitude)
				};
			}
			else if (element is CompleteRelation relation)
			{
				var allBounds = relation.Members.Select(m => AsBounds(m.Member));
				return new Bounds() {
					MaxLatitude = (float)allBounds.Max(n => n.MaxLatitude),
					MinLatitude = (float)allBounds.Min(n => n.MinLatitude),
					MaxLongitude = (float)allBounds.Max(n => n.MaxLongitude),
					MinLongitude = (float)allBounds.Min(n => n.MinLongitude)
				};
			}
			throw new Exception("element wasn't a node, way or relation");
		}

		public static bool IsNodeInBuilding(Node node, ICompleteOsmGeo building)
		{
			if (building is Node buildingNode)
				return DistanceMeters(node, buildingNode) <= 1;
			else if (building is CompleteWay buildingWay)
			{
				var point = new NetTopologySuite.Geometries.Point(node.Longitude.Value, node.Latitude.Value);
				if (buildingWay.Nodes.Length < 4 || (buildingWay.Nodes.First() != buildingWay.Nodes.Last())) return false;
				var ring = new NetTopologySuite.Geometries.LinearRing(
					buildingWay.Nodes.Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToArray());
				var polygon = new NetTopologySuite.Geometries.Polygon(ring);
				return point.Within(polygon);
			}
			else if (building is CompleteRelation buildingRelation)
			{
				// "in" means that even if I land in the center of a donut, I'm still "in" the building.
				// This isn't 100% accurate (false negative) for polygons where the closed outer ring is defined by more than 2 open ways.
				return buildingRelation.Members.Any(m => m.Role != "inner" && IsNodeInBuilding(node, m.Member));
			}
			throw new Exception("ICompleteOsmGeo wasn't a Node, Way or Relation.");
		}

		public static void Nudge(Node[] stack, double north, double east)
		{
			int i = 1;
			foreach (var node in stack.Skip(1))
			{
				node.Longitude += i * north;
				node.Latitude += i * east;
				i++;
			}
		}

		public static Position AsPosition(this ICompleteOsmGeo element)
		{
			if (element is Node node) return new Position(node.Longitude.Value, node.Latitude.Value);
			else if (element is CompleteWay way)
			{
				return new Position(way.Nodes.Average(n => n.Longitude.Value), way.Nodes.Average(n => n.Latitude.Value));
			}
			else if (element is CompleteRelation relation)
			{
				// This isn't exact for relations. Good enough.
				var positions = relation.Members.Select(m => AsPosition(m.Member));

				return new Position(positions.Average(n => n.Longitude), positions.Average(n => n.Latitude));
			}
			throw new Exception("element wasn't a node, way or relation");
		}

		public static double DistanceMeters(ICompleteOsmGeo left, ICompleteOsmGeo right)
		{
			return DistanceMeters(AsPosition(left), AsPosition(right));
		}

		public static double DistanceMeters(Position left, Position right)
		{
			return DistanceMeters(left.Latitude, left.Longitude, right.Latitude, right.Longitude);
		}

		public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
		{
			var averageLat = (lat1 + lat2) / 2;
			var degPerRad = 180 / 3.14;
			var dLonKm = (lon1 - lon2) * 111000 * Math.Cos(averageLat / degPerRad);
			var dLatKm = (lat1 - lat2) * 110000;

			var distance = Math.Sqrt(dLatKm * dLatKm + dLonKm * dLonKm);
			return distance;
		}

		public static double DistanceLat(double distanceMeters)
		{
			// 1 lat degree = 1_000_000/9 meters
			// 1 meter = 9/1_000_000 lat degrees
			return distanceMeters * 9 / 1_000_000;
		}

		public static double DistanceLon(double distanceMeters, double latitude)
		{
			var lon = distanceMeters * 9 / 1_000_000 / Math.Cos(latitude / 180 * Math.PI);

			if (lon < 0 && distanceMeters >= 0) throw new Exception("Bad calculated lon");

			return lon;
		}

		public static bool IsOpenWay(OsmGeo subject)
		{
			return subject is Way subWay2 && subWay2.Nodes.First() != subWay2.Nodes.Last();
		}
	}
}
