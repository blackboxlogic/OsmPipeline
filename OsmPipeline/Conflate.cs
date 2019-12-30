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

			var subjectElements = new OsmGeo[][] { subject.Nodes, subject.Ways, subject.Relations }.SelectMany(e => e);
			var subjectElementsIndexed = subjectElements.ToDictionary(n => n.Type.ToString() + n.Id);
			Merge(reference, subjectElementsIndexed, out List<OsmGeo> create, out List<OsmGeo> modify,
				out List<OsmGeo> delete, out List<OsmGeo> exceptions);
			Log.LogInformation("Starting conflation, matching by address IN a building");
			ApplyNodesToBuildings(subjectElementsIndexed, create, modify, exceptions);

			Log.LogInformation("Writing review files");
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Exceptions.osm", exceptions, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Create.osm", create, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Delete.osm", delete, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Modify.osm", modify, subjectElementsIndexed);
			RemoveReviewTags(create.Concat(modify));
			var change = Fittings.Translate.GeosToChange(create, modify, delete, "OsmPipeline");
			LogSummary(change, exceptions);

			return change;
		}

		private static void LogSummary(OsmChange change, IList<OsmGeo> exceptions)
		{
			Log.LogInformation($"{nameof(change.Create)}: {change.Create.Length}");
			Log.LogInformation($"{nameof(change.Modify)}: {change.Modify.Length}");
			if (change.Delete.Any()) Log.LogWarning($"{nameof(change.Delete)}: {change.Delete.Length}");
			if (exceptions.Any()) Log.LogWarning($"{nameof(exceptions)}: {exceptions.Count}");

			var changeCount = change.Create.Length + change.Modify.Length + change.Delete.Length;
			if (changeCount >= 10_000) // OSM API change set size limit.
			{
				Log.LogError($"ChangeSet size ({changeCount}) is bigger than API's 10,000 limit.");
			}
		}

		private static void RemoveReviewTags(IEnumerable<OsmGeo> elements)
		{
			foreach (var element in elements)
			{
				element.Tags.RemoveKey(Static.maineE911id);
				element.Tags.RemoveKey(Static.maineE911id + ":moved");
				foreach (var tag in element.Tags.Where(t => t.Value.Contains(Static.maineE911id + ":")).ToArray())
				{
					element.Tags.AddOrReplace(tag.Key, tag.Value.Split(Static.maineE911id + ":")[1]);
				}
			}
		}

		private static void WriteToFileWithChildrenIfAny(string fileName, IList<OsmGeo> osmGeos,
			Dictionary<string, OsmGeo> subjectElementsIndexed)
		{
			if (osmGeos.Any())
				FileSerializer.WriteXml(fileName,
					osmGeos.WithChildren(subjectElementsIndexed).Distinct().AsOsm());
		}

		private static void Merge(Osm reference, Dictionary<string, OsmGeo> subjectElementsIndexed, out List<OsmGeo> create,
			out List<OsmGeo> modify, out List<OsmGeo> delete, out List<OsmGeo> exceptions)
		{
			create = new List<OsmGeo>();
			modify = new List<OsmGeo>();
			delete = new List<OsmGeo>();
			exceptions = new List<OsmGeo>();

			var subjectAddressIndex = subjectElementsIndexed.Values
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
						&& !Geometry.IsNodeInBuilding(referenceElement, subjectElement.AsComplete(subjectElementsIndexed)))
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
							tagsChanged = MergeTags(referenceElement, subjectElement);
							tagsChanged |= MoveNode(referenceElement, subjectElement, closestMatch.distance);
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
								Log.LogError("Subject modified again!" + Identify(subjectElement, referenceElement));
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

		private static void ApplyNodesToBuildings(Dictionary<string, OsmGeo> subjectElementsIndexed,
			List<OsmGeo> create, List<OsmGeo> modify, List<OsmGeo> exceptions)
		{
			var nonAddrCompleteBuildings = subjectElementsIndexed.Values.Where(w => IsBuilding(w) && !IsAddressy(w))
				.Select(b => b.AsComplete(subjectElementsIndexed))
				.ToArray();
			var addrSubjectNodes = subjectElementsIndexed.Values.OfType<Node>().Where(n => IsAddressy(n)).ToArray();
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
					MergeTags(node, building);
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

		private static bool MoveNode(OsmGeo reference, OsmGeo subject, double currentDistanceMeters)
		{
			if (subject is Node subjectNode
				&& reference is Node referenceNode
				&& currentDistanceMeters > int.Parse(Static.Config["MinNodeMoveDistance"]))
			{
				subjectNode.Latitude = referenceNode.Latitude;
				subjectNode.Longitude = referenceNode.Longitude;
				// Mark it for easier review
				subject.Tags.AddOrReplace(Static.maineE911id + ":moved", (int)currentDistanceMeters + " meters");
				return true;
			}

			return false;
		}

		private static bool MergeTags(OsmGeo reference, OsmGeo subject)
		{
			var changed = false;

			foreach (var refTag in reference.Tags)
			{
				if (subject.Tags.TryGetValue(refTag.Key, out string subValue))
				{
					if (subValue != refTag.Value)
					{
						if (refTag.Key == Static.maineE911id)
						{
							subject.Tags.AddOrReplace(refTag.Key, refTag.Value + ";" + subValue);
						}
						else if (TagTree.Keys.ContainsKey(refTag.Key) &&
							TagTree.Keys[refTag.Key].IsDecendantOf(refTag.Value, subValue))
						{
							// Make building tag MORE specific. Marked for easier review.
							subject.Tags.AddOrReplace(refTag.Key, subValue + ":" + Static.maineE911id + ":" + refTag.Value);
							changed = true;
						}
						else if (TagTree.Keys.ContainsKey(refTag.Key) &&
							TagTree.Keys[refTag.Key].IsDecendantOf(subValue, refTag.Value))
						{
							// Nothing, it is already more specific.
						}
						else
						{
							throw new Exception("A tag conflict!" + Identify(refTag.Key, subject, reference));
						}
					}
				}
				else if(refTag.Key == Static.maineE911id)
				{
					subject.Tags.Add(refTag);
				}
				else
				{
					changed = true;
					// Marking for easier review. Prefix will be removed later.
					subject.Tags.Add(refTag.Key, Static.maineE911id + ":" + refTag.Value);
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
