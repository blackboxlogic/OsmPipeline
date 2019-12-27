using BAMCIS.GeoJSON;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Complete;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Geometry
	{
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

		public static Dictionary<ICompleteOsmGeo, Node[]> NodesInCompleteElements(ICompleteOsmGeo[] buildings, Node[] nodes)
		{
			return buildings.ToDictionary(b => b, b => nodes.Where(n => IsNodeInBuilding(n, b)).ToArray())
				.Where(kvp => kvp.Value.Any())
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}

		public static bool IsNodeInBuilding(Node node, ICompleteOsmGeo building)
		{
			if (building is Node buildingNode)
				return node.Latitude == buildingNode.Latitude && node.Longitude == buildingNode.Longitude;
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
				// This isn't accurate for polygons where the closed outer ring is defined by more than 2 open ways.
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
	}
}
