using BAMCIS.GeoJSON;
using OsmSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline
{
	public static class Geometry
	{
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

		public static Position AsLocation(Node node)
		{
			return new Position(node.Longitude.Value, node.Latitude.Value);
		}

		public static double DistanceMeters(OsmGeo left, OsmGeo right, Dictionary<long, Node> allNodes, Dictionary<long, Way> allWays)
		{
			return DistanceMeters(GetCentroid(left, allNodes, allWays), GetCentroid(right, allNodes, allWays));
		}

		public static Position GetCentroid(OsmGeo element, Dictionary<long, Node> allNodes, Dictionary<long, Way> allWays)
		{
			if (element is Node node)
			{
				return new Position(node.Longitude.Value, node.Latitude.Value);
			}
			else if (element is Way way)
			{
				var nodes = way.Nodes.Select(nid => allNodes[nid]).ToArray();
				return new Position(nodes.Average(n => n.Longitude.Value), nodes.Average(n => n.Latitude.Value));
			}
			else if (element is Relation relation)
			{
				// Assumes position basedon relation's ways (ignoring nodes or child relations)
				var nodes = relation.Members
					.Where(m => m.Type == OsmGeoType.Way)
					.SelectMany(m => allWays[m.Id].Nodes)
					.Select(nid => allNodes[nid])
					.ToArray();
				return new Position(nodes.Average(n => n.Longitude.Value), nodes.Average(n => n.Latitude.Value));
			}
			throw new Exception("element wasn't a node, way or relation");
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
