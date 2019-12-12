using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Linq;
using System.Collections.Generic;
using OsmSharp.Tags;
using OsmSharp;
using Microsoft.Extensions.Logging;
using OsmSharp.Complete;
using OsmPipeline.Fittings;

namespace OsmPipeline
{
	public static class Conflate
	{
		private static ILogger Log;

		public static OsmChange Merge(
			ILoggerFactory loggerFactory, Osm reference, Osm subject, string scopeName)
		{
			Log = Log ?? loggerFactory.CreateLogger(typeof(Conflate));
			Merge(reference, subject, out List<OsmGeo> create, out List<OsmGeo> modify,
				out List<OsmGeo> delete, out List<OsmGeo> exceptions);
			ApplyNodesToBuildings(subject, create, modify);
			FileSerializer.WriteXml(scopeName + "Conflated.Exceptions.osm", exceptions.AsOsm());
			FileSerializer.WriteXml(scopeName + "Conflated.Create.osm", create.AsOsm());
			//FileSerializer.WriteXml(scopeName + "Conflated.Delete.osm", WithNodes(delete, subject.Nodes).AsOsm());
			FileSerializer.WriteXml(scopeName + "Conflated.Modify.osm", WithNodes(modify, subject.Nodes).AsOsm());

			foreach (var element in create.Concat(modify))
			{
				element.Tags.RemoveKey("maineE911id");
			}

			var change = Fittings.Translate.GeosToChange(create, modify, delete, "OsmPipeline");

			return change;
		}

		private static void Merge(Osm reference, Osm subject, out List<OsmGeo> create,
			out List<OsmGeo> modify, out List<OsmGeo> delete, out List<OsmGeo> exceptions)
		{
			// Catch exceptions? and put items into excpetions list, with a NOTE about what why.

			create = new List<OsmGeo>();
			modify = new List<OsmGeo>();
			delete = new List<OsmGeo>();
			exceptions = new List<OsmGeo>(); // put stuff in here.

			var subjectNodesById = subject.Nodes.ToDictionary(n => n.Id.Value);
			var subjectWaysById = subject.Ways.ToDictionary(n => n.Id.Value);

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
						Log.LogWarning($"Multiple matches!" + Identify(referenceElement) + Identify(subjectElements));
					}
					var refCentroid = Geometry.GetCentroid(referenceElement, subjectNodesById, subjectWaysById);
					var closestMatch = subjectElements
						.Select(element => new { element, centroid = Geometry.GetCentroid(element, subjectNodesById, subjectWaysById) })
						.Select(match => new { match.element, distance = Geometry.DistanceMeters(refCentroid, match.centroid) })
						.OrderBy(match => match.distance).First();
					// Maybe choose 'best' by "doesn't have any tag conflicts"?
					// Maybe every instance should be manually reviewed... files stores [mergeWith, ignore, addNew]
					var subjectElement = closestMatch.element;
					if (closestMatch.distance > 100)
					{
						Log.LogWarning($"Matched, but too far: {(int)closestMatch.distance} > 100 meters.{Identify(subjectElement, referenceElement)}");
						create.Add(referenceElement);
					}
					else if (MergeTags(referenceElement, subjectElement, closestMatch.distance))
					{
						if (modify.Any(n => n.Id == subjectElement.Id))
							Log.LogWarning("Subject modified again!" + Identify(subjectElement, referenceElement));
						subjectElement.Version++;
						modify.Add(subjectElement);
					}
				}
				else
				{
					create.Add(referenceElement);
				}
			}
		}

		private static void ApplyNodesToBuildings(Osm subject, List<OsmGeo> create, List<OsmGeo> modify)
		{
			var subjectNodesByID = subject.Nodes.ToDictionary(n => n.Id);
			var subjectWaysByID = subject.Ways.ToDictionary(n => n.Id);
			var nonAddrBuildings = subject.Ways
				.Where(w => IsBuilding(w) && !IsAddressy(w))
				.Select(b => new CompleteWay() { Id = b.Id.Value, Nodes = b.Nodes.Select(n => subjectNodesByID[n]).ToArray() })
				.ToArray();
			var addrSubjectNodes = subject.Nodes.Where(n => IsAddressy(n)).ToArray();
			var buildingsWithOldAddrNodes = Geometry.NodesInBuildings(nonAddrBuildings, addrSubjectNodes);
			var buildingsToNewAddr = Geometry.NodesInBuildings(nonAddrBuildings, create.OfType<Node>().ToArray());

			var addrMerges = buildingsToNewAddr
				.Where(b => !buildingsWithOldAddrNodes.ContainsKey(b.Key) && b.Value.Length == 1)
				.Select(kvp => new { Building = subjectWaysByID[kvp.Key.Id], Node = kvp.Value.First() })
				.ToArray();

			foreach (var addrMerge in addrMerges)
			{
				create.Remove(addrMerge.Node);
				addrMerge.Building.Tags.AddOrReplace(addrMerge.Node.Tags); // Consider noting conflicts.
				modify.Add(addrMerge.Building);
			}
		}

		// Should handle relations too!
		private static IEnumerable<OsmGeo> WithNodes(List<OsmGeo> source, Node[] nodes)
		{
			var wayNodes = new HashSet<long>(source.OfType<Way>().SelectMany(w => w.Nodes));
			return source.Concat(nodes.Where(n => wayNodes.Contains(n.Id.Value)));
		}

		private static string Identify(params OsmGeo[] elements)
		{
			return "\n\t" + string.Join("\n\t", elements.Select(e => $"{e.Type.ToString().ToLower()}/{e.Id}"));
		}

		private static string Identify(string key, params OsmGeo[] elements)
		{
			return "\n\t" + string.Join("\n\t", elements.Select(e => $"{e.ToString().ToLower()}/{e.Id}.{key}={e.Tags[key]}"));
		}

		private static bool IsAddressy(OsmGeo element)
		{
			return element.Tags?.Any(t => t.Key.StartsWith("addr:")) == true;
		}

		private static bool IsBuilding(Way way)
		{
			return way.Tags?.ContainsKey("building") == true;
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
						Log.LogError("A tag conflict! Kept subject's tag." + Identify(tag.Key, subject, reference));
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

		private static TagsCollection GetAddrTags(this OsmGeo element, bool all = false)
		{
			return all
				? new TagsCollection(element.Tags?.Where(t => t.Key.StartsWith("addr:")) ?? new Tag[0])
				: new TagsCollection(element.Tags?.Where(t => t.Key == "addr:street" || t.Key == "addr:housenumber") ?? new Tag[0]);
		}
	}
}
