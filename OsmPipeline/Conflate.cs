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

		public static OsmChange Merge(Osm reference, Osm subject, string scopeName)
		{
			Log = Log ?? Static.LogFactory.CreateLogger(typeof(Conflate));
			Log.LogInformation("Starting conflation, matching by address tags");
			Merge(reference, subject, out List<OsmGeo> create, out List<OsmGeo> modify,
				out List<OsmGeo> delete, out List<OsmGeo> exceptions);
			Log.LogInformation("Starting conflation, matching by address IN a building");
			ApplyNodesToBuildings(subject, create, modify, exceptions);
			FileSerializer.WriteXml(scopeName + "/Conflated.Exceptions.osm", exceptions.AsOsm());
			FileSerializer.WriteXml(scopeName + "/Conflated.Create.osm", create.AsOsm());
			//FileSerializer.WriteXml(scopeName + "/Conflated.Delete.osm", WithNodes(delete, subject.Nodes).AsOsm());
			FileSerializer.WriteXml(scopeName + "/Conflated.Modify.osm", WithNodes(modify, subject.Nodes).AsOsm());

			foreach (var element in create.Concat(modify))
			{
				element.Tags.RemoveKey(Static.maineE911id);
			}

			var changes = create.Count + modify.Count + delete.Count;
			if (changes > 10_000) // OSM API change set size limit.
			{
				throw new Exception($"ChangeSet size ({changes}) is bigger than API's 10,000 limit");
			}

			var change = Fittings.Translate.GeosToChange(create, modify, delete, "OsmPipeline");

			return change;
		}

		private static void Merge(Osm reference, Osm subject, out List<OsmGeo> create,
			out List<OsmGeo> modify, out List<OsmGeo> delete, out List<OsmGeo> exceptions)
		{
			create = new List<OsmGeo>();
			modify = new List<OsmGeo>();
			delete = new List<OsmGeo>();
			exceptions = new List<OsmGeo>();

			var subjectElements = new OsmGeo[][] { subject.Nodes, subject.Ways, subject.Relations }.SelectMany(e => e);
			var subjectElementsIndexed = subjectElements.ToDictionary(n => n.Type.ToString() + n.Id);

			var subjectAddressIndex = subjectElements
				.GroupBy(e => GetAddrTags(e, false))
				.Where(g => g.Key.Any())
				.ToDictionary(g => g.Key, g => g.ToArray());

			foreach (var referenceElement in reference.Nodes)
			{
				if (referenceElement.Tags["addr:housenumber"] == "0") // indicates there was a missing address.
				{
					referenceElement.Tags["exception"] = $"Missing house number!";
					exceptions.Add(referenceElement);
					continue;
				}

				var addr = referenceElement.GetAddrTags();
				if (subjectAddressIndex.TryGetValue(addr, out var targetSubjectElements))
				{
					if (targetSubjectElements.Length > 1)
					{
						// Could try to auto resolve multi-matches by checking tag conflicts or if I'm IN one of them.
						referenceElement.Tags["exception"] = $"Multiple matches!" + Identify(targetSubjectElements);
						exceptions.Add(referenceElement);
						continue;
					}
					var refCentroid = Geometry.AsPosition(referenceElement.AsComplete(subjectElementsIndexed));
					var closestMatch = targetSubjectElements
						.Select(element => new { element, centroid = Geometry.AsPosition(element.AsComplete(subjectElementsIndexed)) })
						.Select(match => new { match.element, distance = Geometry.DistanceMeters(refCentroid, match.centroid) })
						.OrderBy(match => match.distance).First();
					var subjectElement = closestMatch.element;
					if (closestMatch.distance > int.Parse(Static.Config["MatchDistanceKmMaz"])
						&& !Geometry.IsNodeInBuilding(referenceElement, ((Way)subjectElement).AsComplete(subjectElementsIndexed)))
					{
						referenceElement.Tags["exception"] = $"Matched, but too far: {(int)closestMatch.distance} > 100 meters.{Identify(subjectElement)}";
						exceptions.Add(referenceElement);
						continue;
					}
					else
					{
						bool tagsChanged;
						try
						{
							tagsChanged = MergeTags(referenceElement, subjectElement, closestMatch.distance);
						}
						catch (Exception e)
						{
							referenceElement.Tags["exception"] = e.Message;
							exceptions.Add(referenceElement);
							continue;
						}

						if (tagsChanged)
						{
							if (modify.Any(n => n.Id == subjectElement.Id))
								Log.LogWarning("Subject modified again!" + Identify(subjectElement, referenceElement));
							subjectElement.Version++;
							modify.Add(subjectElement);
						}
					}
				}
				else
				{
					create.Add(referenceElement);
				}
			}
		}

		private static void ApplyNodesToBuildings(Osm subject, List<OsmGeo> create, List<OsmGeo> modify, List<OsmGeo> exceptions)
		{
			var subjectElements = new OsmGeo[][] { subject.Nodes, subject.Ways, subject.Relations }.SelectMany(e => e);
			var subjectElementsIndexed = subjectElements.ToDictionary(n => n.Type.ToString() + n.Id);
			var nonAddrCompleteBuildings = subjectElements.Where(w => IsBuilding(w) && !IsAddressy(w))
				.Select(b => b.AsComplete(subjectElementsIndexed))
				.ToArray();
			var addrSubjectNodes = subject.Nodes.Where(n => IsAddressy(n)).ToArray();
			var completeBuildingsWithOldAddrNodes = Geometry.NodesInCompleteElements(nonAddrCompleteBuildings, addrSubjectNodes);
			nonAddrCompleteBuildings = nonAddrCompleteBuildings.Where(b => !completeBuildingsWithOldAddrNodes.ContainsKey(b)).ToArray();
			var completeBuildingsWithNewAddrNodes = Geometry.NodesInCompleteElements(nonAddrCompleteBuildings, create.OfType<Node>().ToArray());

			foreach (var completeBuildingToNodes in completeBuildingsWithNewAddrNodes)
			{
				if (completeBuildingToNodes.Value.Length > 1) continue; // multiple matches, leave it alone.
				var node = completeBuildingToNodes.Value.First();
				var building = subjectElementsIndexed[completeBuildingToNodes.Key.Type.ToString() + completeBuildingToNodes.Key.Id];
				create.Remove(node);

				try
				{
					MergeTags(node, building, 0);
				}
				catch (Exception e)
				{
					node.Tags["exception"] = e.Message;
					exceptions.Add(node);
					continue;
				}

				building.Version++;
				modify.Add(building);
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
			return "\n\t" + string.Join("\n\t", elements.Select(e => $"{e.Type.ToString().ToLower()}/{e.Id}.{key}={e.Tags[key]}"));
		}

		private static bool IsAddressy(OsmGeo element)
		{
			return element.Tags?.Any(t => t.Key.StartsWith("addr:")) == true;
		}

		private static bool IsBuilding(OsmGeo element)
		{
			return element.Tags?.ContainsKey("building") == true;
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

			foreach (var refTag in reference.Tags)
			{
				if (subject.Tags.TryGetValue(refTag.Key, out string subValue))
				{
					if (subValue != refTag.Value &&
						refTag.Key != Static.maineE911id &&
						TagTree.Keys.ContainsKey(refTag.Key) &&
						!TagTree.Keys[refTag.Key].IsDecendantOf(refTag.Value, subValue))
					{
						throw new Exception("A tag conflict!" + Identify(refTag.Key, subject, reference));
					}
				}
				else
				{
					changed = true;
					subject.Tags.Add(refTag);
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
