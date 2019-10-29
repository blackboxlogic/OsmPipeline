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

namespace OsmPipeline
{
	public static class Conflate
	{
		public static async Task<OsmChange> FetchAndMerge(OsmGeo scope, string scopeName, Osm reference)
		{
			var bounds = await GetBoundingBox(scope);
			var subject = FileSerializer.ReadXmlCacheOrSource(
				scopeName + "OsmCache.osm", () => GetElementsInBoundingBox(bounds)).Result;
			List<OsmGeo> create = new List<OsmGeo>();
			List<OsmGeo> modify = new List<OsmGeo>();

			OsmChange change = new OsmChange();
			var subjectIndex = subject.Nodes.GroupBy(GetAddrTags)
				.Where(g => g.Key.Any())
				.ToDictionary(g => g.Key, g => g.ToArray());

			foreach (var source in reference.Nodes)
			{
				var addr = source.GetAddrTags();
				if (subjectIndex.TryGetValue(addr, out var matches))
				{
					var match = matches.Single();
					if (MergeTags(source, match))
					{
						match.Version++;
						modify.Add(match);
					}
				}
				else
				{
					create.Add(source);
				}
			}

			return new OsmChange() { Create = create.ToArray(), Modify = modify.ToArray() };
		}

		private static bool MergeTags(OsmGeo reference, OsmGeo subject)
		{
			var changed = false;

			if (subject is Node subjectNode && reference is Node referenceNode)
			{
				var distance = DistanceMeters(subjectNode, referenceNode); // Gee, or distance from centroid?
				if (distance > 100)
				{
					return false; // but then it should be added, right?
				}
				if (distance > 1)
				{
					subjectNode.Latitude = referenceNode.Latitude;
					subjectNode.Longitude = referenceNode.Longitude;
					changed = true;
				}
			}

			foreach (var tag in reference.Tags)
			{
				if (subject.Tags.TryGetValue(tag.Key, out string value))
				{
					if (value != tag.Value)
					{
						// middle school name conflict, alt name.
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

		// This is close enough.
		public static double DistanceMeters(Node left, Node right)
		{
			var averageLat = ((left.Latitude + right.Latitude) / 2).Value;
			var degPerRad = 180 / 3.14;
			var dLonKm = (left.Longitude.Value - right.Longitude.Value) * 111000 * Math.Cos(averageLat / degPerRad);
			var dLatKm = (left.Latitude.Value - right.Latitude.Value) * 110000;

			var distance = Math.Sqrt(dLatKm * dLatKm + dLonKm * dLonKm);
			return distance;
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

		private static TagsCollection GetAddrTags(this OsmGeo element)
		{
			return new TagsCollection(element.Tags?.Where(t => t.Key.StartsWith("addr:")) ?? new Tag[0]);
		}
	}
}
