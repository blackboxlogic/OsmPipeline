﻿using BAMCIS.GeoJSON;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Complete;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Geometry
	{
		public static IEnumerable<Bounds> Quarter(this Bounds bounds)
		{
			var halfLat = (bounds.MaxLatitude + bounds.MinLatitude) / 2;
			var halfLon = (bounds.MaxLongitude + bounds.MinLongitude) / 2;
			yield return new Bounds()
			{
				MaxLatitude = bounds.MaxLatitude,
				MaxLongitude = bounds.MaxLongitude,
				MinLatitude = halfLat,
				MinLongitude = halfLon
			};
			yield return new Bounds()
			{
				MaxLatitude = halfLat,
				MaxLongitude = halfLon,
				MinLatitude = bounds.MinLatitude,
				MinLongitude = bounds.MinLongitude
			};
			yield return new Bounds()
			{
				MaxLatitude = bounds.MaxLatitude,
				MaxLongitude = halfLon,
				MinLatitude = halfLat,
				MinLongitude = bounds.MinLongitude
			};
			yield return new Bounds()
			{
				MaxLatitude = halfLat,
				MaxLongitude = bounds.MaxLongitude,
				MinLatitude = bounds.MinLatitude,
				MinLongitude = halfLon
			};
		}

		public static char GetDirectionArrow(Position from, Position to)
		{
			var theta = Math.Atan2(to.Latitude - from.Latitude, to.Longitude - from.Longitude);
			var slice = (int)((theta + Math.PI) / (2 * Math.PI) * 16);
			var arrow = "←↙↙↓↓↘↘→→↗↗↑↑↖↖←"[slice];
			return arrow;
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

		public static Dictionary<ICompleteOsmGeo, Node[]> NodesInOrNearCompleteElements(
			ICompleteOsmGeo[] buildings, Node[] nodes, double distanceMeters, out Node[] multiMatches)
		{
			var inside = NodesInCompleteElements(buildings, nodes);

			buildings = buildings.Except(inside.Keys).ToArray();
			nodes = nodes.Except(inside.SelectMany(r => r.Value)).ToArray();
			var near = NodesNearCompleteElements(buildings, nodes, distanceMeters, out multiMatches);

			return inside.Concat(near).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}

		public static Dictionary<ICompleteOsmGeo, Node[]> NodesInCompleteElements(ICompleteOsmGeo[] buildings, Node[] nodes)
		{
			var byLat = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Latitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));
			var byLon = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Longitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));

			var result = new Dictionary<ICompleteOsmGeo, Node[]>();
			foreach (var building in buildings)
			{
				var bounds = building.AsBounds();
				var candidates = FastNodesInBounds(bounds, byLat, byLon);
				var inners = candidates.Where(n => IsNodeInBuilding(n, building)).ToArray();
				if (inners.Any()) result.Add(building, inners);
			}

			return result;
		}

		// Can return mutliple buildings having the same node near them!
		public static Dictionary<ICompleteOsmGeo, Node[]> NodesNearCompleteElements(
			ICompleteOsmGeo[] elements, Node[] nodes, double withinMeters,
			out Node[] multiMatches)
		{
			var byLat = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Latitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));
			var byLon = new SortedList<double, Node[]>(nodes.GroupBy(n => n.Longitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));

			var result = new Dictionary<ICompleteOsmGeo, Node[]>();
			
			foreach (var element in elements)
			{
				var bounds = element.AsBounds(withinMeters);
				var candidates = FastNodesInBounds(bounds, byLat, byLon);
				var inners = candidates.Where(n => DistanceMeters(element, n) <= withinMeters).ToArray();
				if (inners.Any()) result.Add(element, inners);
			}

			multiMatches = RemoveDuplicateValues(result);

			return result;
		}

		// Given a dictionary<K, V[]>, remove an V that appear in multiple keys. Remove any keys that are empty.
		private static V[] RemoveDuplicateValues<K, V>(Dictionary<K, V[]> dictionary)
		{
			var nodesInMultipleBuildings = dictionary.SelectMany(b => b.Value)
				.GroupBy(n => n).Where(n => n.Count() > 1).SelectMany(n => n).ToArray();
			foreach (var kvp in dictionary.ToArray())
			{
				var newNodes = kvp.Value.Except(nodesInMultipleBuildings).ToArray();
				if (newNodes.Any())
				{
					dictionary[kvp.Key] = kvp.Value.Except(nodesInMultipleBuildings).ToArray();
				}
				else
				{
					dictionary.Remove(kvp.Key);
				}
			}

			return nodesInMultipleBuildings;
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

		private static Bounds AsBounds(this ICompleteOsmGeo element, double bufferMeters = 0)
		{
			var bufferLat = bufferMeters == 0 ? 0f : (float)DistanceLat(bufferMeters);
			var bufferLon = bufferMeters == 0 ? 0f : (float)DistanceLon(bufferMeters, element.AsPosition().Latitude);

			if (element is Node node)
			{
				var bounds = new Bounds();
				bounds.MaxLatitude = (float)node.Latitude + bufferLat;
				bounds.MinLatitude = (float)node.Latitude - bufferLat;
				bounds.MaxLongitude = (float)node.Longitude + bufferLon;
				bounds.MinLongitude = (float)node.Longitude - bufferLon;
				return bounds;
			}
			else if (element is CompleteWay way)
			{
				var bounds = new Bounds();
				bounds.MaxLatitude = (float)way.Nodes.Max(n => n.Latitude) + bufferLat;
				bounds.MinLatitude = (float)way.Nodes.Min(n => n.Latitude) - bufferLat;
				bounds.MaxLongitude = (float)way.Nodes.Max(n => n.Longitude) + bufferLon;
				bounds.MinLongitude = (float)way.Nodes.Min(n => n.Longitude) - bufferLon;
				return bounds;
			}
			else if (element is CompleteRelation relation)
			{
				var allBounds = relation.Members.Select(m => AsBounds(m.Member));
				var bounds = new Bounds();
				bounds.MaxLatitude = (float)allBounds.Max(n => n.MaxLatitude) + bufferLat;
				bounds.MinLatitude = (float)allBounds.Min(n => n.MinLatitude) - bufferLat;
				bounds.MaxLongitude = (float)allBounds.Max(n => n.MaxLongitude) + bufferLon;
				bounds.MinLongitude = (float)allBounds.Min(n => n.MinLongitude) - bufferLon;
				return bounds;
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
				var ring = new NetTopologySuite.Geometries.LinearRing(
					buildingWay.Nodes.Append(buildingWay.Nodes[0]).Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToArray());
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
			// 1 lat degree = 100_000/9 meters
			// 1 meter = 9/100_000 lat degrees
			return distanceMeters * 9 / 100_000;
		}

		public static double DistanceLon(double distanceMeters, double latitude)
		{
			return distanceMeters * 100_000 / 9 * Math.Cos(latitude);
		}
	}
}
