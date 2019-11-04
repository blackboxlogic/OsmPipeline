using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Linq;
using System.Collections.Generic;
using OsmSharp.Tags;
using OsmSharp;
using System.Threading.Tasks;
using OsmPipeline.Fittings;
using System.Net.Http;
using OsmSharp.IO.API;
using BAMCIS.GeoJSON;
using Microsoft.Extensions.Logging;

namespace OsmPipeline
{
	public static class Conflate
	{
		private static ILogger Log;

		public static async Task<OsmChange> FetchAndMerge(
			ILoggerFactory loggerFactory, OsmGeo scope, string scopeName, Osm reference)
		{
			Log = Log ?? loggerFactory.CreateLogger(typeof(Conflate));
			List<OsmGeo> create = new List<OsmGeo>();
			List<OsmGeo> modify = new List<OsmGeo>();

			var bounds = await FileSerializer.ReadXmlCacheOrSource(
				scopeName + "Bounds.xml", () => GetBoundingBox(scope));
			var subject = await FileSerializer.ReadXmlCacheOrSource(
				scopeName + "OsmCache.osm", () => GetElementsInBoundingBox(bounds));

			var subjectNodesById = subject.Nodes.ToDictionary(n => n.Id.Value);
			var subjectWaysById = subject.Ways.ToDictionary(n => n.Id.Value);

			OsmChange change = new OsmChange();
			var subjectIndex = new OsmGeo[][] { subject.Nodes, subject.Ways, subject.Relations }
				.SelectMany(e => e)
				.GroupBy(e => GetAddrTags(e, false))
				.Where(g => g.Key.Any())
				.ToDictionary(g => g.Key, g => g.ToArray());

			foreach (var referenceElement in reference.Nodes)
			{
				var addr = referenceElement.GetAddrTags();
				if (subjectIndex.TryGetValue(addr, out var subjectElements))
				{
					if (subjectElements.Length > 1)
					{
						Log.LogWarning("Multiple matches!");
					}
					var subjectElement = subjectElements.First();
					var distance = DistanceMeters(
						GetCentroid(referenceElement, subjectNodesById, subjectWaysById),
						GetCentroid(subjectElement, subjectNodesById, subjectWaysById));
					if (distance > 100)
					{
						Log.LogWarning("Address match is over 100 meters, ignoring the match");
						create.Add(referenceElement);
					}
					else if (MergeTags(referenceElement, subjectElement, distance))
					{
						subjectElement.Version++;
						modify.Add(subjectElement);
					}
					else
					{
						Log.LogInformation("The tag merge didn't have any effect on the subject");
					}
				}
				else
				{
					create.Add(referenceElement);
				}
			}

			return new OsmChange() { Create = create.ToArray(), Modify = modify.ToArray() };
		}

		private static bool MergeTags(OsmGeo reference, OsmGeo subject, double distanceMeters)
		{
			var changed = false;

			if (subject is Node subjectNode && reference is Node referenceNode)
			{
				if (distanceMeters > 1)
				{
					subjectNode.Latitude = referenceNode.Latitude;
					subjectNode.Longitude = referenceNode.Longitude;
					changed = true;
				}
			}

			foreach (var tag in reference.Tags.Where(t => t.Key != "maineE911id"))
			{
				if (subject.Tags.TryGetValue(tag.Key, out string value))
				{
					if (value != tag.Value)
					{
						Log.LogError("A tag conflict!"); // middle school name conflict, alt name?
					}
				}
				else
				{
					changed = true;
					subject.Tags.Add(tag);
				}
			}

			return changed;
		}

		public static async Task<Osm> GetElementsInBoundingBox(Bounds bounds)
		{
			var osmApiClient = new NonAuthClient("https://www.openstreetmap.org/api/",
				new HttpClient(), null);
			var map = await osmApiClient.GetMap(bounds);
			return map;
		}

		public static async Task<Bounds> GetBoundingBox(OsmGeo scope)
		{
			var nominatim = new OsmNominatimClient("OsmPipeline", "https://nominatim.openstreetmap.org", "blackboxlogic@gmail.com");
			var target = await nominatim.Lookup(scope.Type.ToString()[0].ToString() + scope.Id.Value);
			return new Bounds()
			{
				MinLatitude = target[0].boundingbox[0],
				MaxLatitude = target[0].boundingbox[1],
				MinLongitude = target[0].boundingbox[2],
				MaxLongitude = target[0].boundingbox[3]
			};
		}

		private static TagsCollection GetAddrTags(this OsmGeo element, bool all = false)
		{
			return all
				? new TagsCollection(element.Tags?.Where(t => t.Key.StartsWith("addr:")) ?? new Tag[0])
				: new TagsCollection(element.Tags?.Where(t => t.Key == "addr:street" || t.Key == "addr:housenumber") ?? new Tag[0]);
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
			else
			{
				throw new Exception("Why is element a relation?");
			}
		}

		public static double DistanceMeters(Position left, Position right)
		{
			return DistanceMeters(left.Latitude, left.Longitude, right.Latitude, right.Longitude);
		}

		//public static double DistanceMeters(Node left, Node right)
		//{
		//	return DistanceMeters(left.Latitude.Value, left.Longitude.Value, right.Latitude.Value, right.Longitude.Value);
		//}

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
