using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Linq;
using System.Collections.Generic;
using OsmSharp.Tags;
using OsmSharp;
using Microsoft.Extensions.Logging;
using OsmSharp.Complete;

namespace OsmPipeline
{
	public static class Conflate
	{
		private static ILogger Log;

		public static OsmChange Merge(
			ILoggerFactory loggerFactory, Osm reference, Osm subject)
		{
			Log = Log ?? loggerFactory.CreateLogger(typeof(Conflate));
			List<OsmGeo> create = new List<OsmGeo>();
			List<OsmGeo> modify = new List<OsmGeo>();

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
						Log.LogWarning($"Multiple matches!");
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
						Log.LogWarning($"Matched, but too far: {(int)closestMatch.distance} > 100 meters.\n{subjectElement.Type}:{subjectElement.Id}\n{referenceElement.Type}:{referenceElement.Id}");
						create.Add(referenceElement);
					}
					else if (MergeTags(referenceElement, subjectElement, closestMatch.distance))
					{
						if (modify.Any(n => n.Id == subjectElement.Id))
							Log.LogWarning($"Modified {subjectElement.Type}:{subjectElement.Id} again by {referenceElement.Type}:{referenceElement.Id}!");
						subjectElement.Version++;
						modify.Add(subjectElement);
					}
				}
				else
				{
					create.Add(referenceElement);
				}
			}

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
				addrMerge.Building.Tags.AddOrReplace(addrMerge.Node.Tags);
				modify.Add(addrMerge.Building);
			}

			return Fittings.Translate.GeosToChange(create, modify, null, "OsmPipeline");
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
						Log.LogError($"A tag conflict! Kept subject's tag.\n{subject.Type}:{subject.Id}.{tag.Key}={value}\n{reference.Type}:{reference.Id}.{tag.Key}={tag.Value}");
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
