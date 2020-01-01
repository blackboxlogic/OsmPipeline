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
			LogOffset(modify);
			RemoveReviewTags(create.Concat(modify));
			var change = Fittings.Translate.GeosToChange(create, modify, delete, "OsmPipeline");
			LogSummary(change, exceptions);

			return change;
		}

		private static void LogOffset(IList<OsmGeo> modify)
		{
			// Try to detect if the data is geographically shifted
			var moved = Static.maineE911id + ":moved";
			var arrowsSummary = modify
				.Where(m => m.Tags.ContainsKey(moved))
				.Select(m => m.Tags[moved].Last())
				.GroupBy(c => c)
				.ToDictionary(c => c.Key, c => c.Count());

			if (arrowsSummary.Values.Max() + 1 > 10 * arrowsSummary.Values.Min() + 1)
			{
				Log.LogWarning("We've detected there might be an offset!");
			}
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
				element.Tags.RemoveAll(tag => tag.Key.StartsWith(Static.maineE911id));
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
				.GroupBy(e => Tags.GetAddrTags(e, false))
				.Where(g => g.Key.Any())
				.ToDictionary(g => g.Key, g => g.ToArray());

			foreach (var referenceElement in reference.Nodes)
			{
				if (referenceElement.Tags["addr:housenumber"] == "0") // indicates there was a missing address.
				{
					referenceElement.Tags.AddOrAppend(Static.maineE911id + ":exception", "Missing house number!");
					exceptions.Add(referenceElement);
					continue;
				}

				var addr = referenceElement.GetAddrTags();
				if (subjectAddressIndex.TryGetValue(addr, out var targetSubjectElements))
				{
					if (targetSubjectElements.Length > 1)
					{
						// Could try to auto resolve multi-matches by checking tag conflicts or if I'm IN one of them.
						referenceElement.Tags.AddOrAppend(Static.maineE911id + ":exception", "Multiple matches!" + Identify(targetSubjectElements));
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
						referenceElement.Tags.AddOrAppend(Static.maineE911id + ":exception", $"Matched, but too far: {(int)closestMatch.distance} meters.{Identify(subjectElement)}");
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
							referenceElement.Tags.AddOrAppend(Static.maineE911id + ":conflict", e.Message);
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
			var buildings = subjectElementsIndexed.Values.Where(w => Tags.IsBuilding(w)) // !Tags.IsAddressy(w)
				.Select(b => b.AsComplete(subjectElementsIndexed))
				.ToArray();
			var newNodes = create.OfType<Node>().ToArray();
			var buildingsAndInnerNewNodes = Geometry.NodesInCompleteElements(buildings, newNodes);

			foreach (var buildingAndInners in buildingsAndInnerNewNodes)
			{
				if (buildingAndInners.Value.Length > 1) continue; // multiple matches, leave it alone.
				var node = buildingAndInners.Value.First();
				var building = subjectElementsIndexed[buildingAndInners.Key.Type.ToString() + buildingAndInners.Key.Id];

				try
				{
					MergeTags(node, building);
					building.Version++;
					create.Remove(node);
					modify.Add(building);
				}
				catch (Exception e)
				{
					node.Tags.AddOrAppend(Static.maineE911id + ":conflict", e.Message);
					exceptions.Add(node);
				}
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

		private static bool MoveNode(OsmGeo reference, OsmGeo subject, double currentDistanceMeters)
		{
			if (subject is Node subjectNode
				&& reference is Node referenceNode
				&& currentDistanceMeters > int.Parse(Static.Config["MinNodeMoveDistance"]))
			{
				var arrow = Geometry.GetDirectionArrow(subjectNode, referenceNode);
				subjectNode.Latitude = referenceNode.Latitude;
				subjectNode.Longitude = referenceNode.Longitude;
				// Mark it for easier review
				subject.Tags.AddOrReplace(Static.maineE911id + ":moved", (int)currentDistanceMeters + " meters " + arrow);
				return true;
			}

			return false;
		}

		private static bool MergeTags(OsmGeo reference, OsmGeo subject)
		{
			var conflicts = new List<string>();
			var changed = false;

			foreach (var refTag in reference.Tags)
			{
				if (refTag.Key.StartsWith(Static.maineE911id))
				{
					subject.Tags.AddOrAppend(refTag.Key, refTag.Value);
				}
				else if (subject.Tags.TryGetValue(refTag.Key, out string subValue))
				{
					if (subValue != refTag.Value)
					{
						if (TagTree.Keys.ContainsKey(refTag.Key) &&
							TagTree.Keys[refTag.Key].IsDecendantOf(refTag.Value, subValue))
						{
							// Make building tag MORE specific. Marked for easier review.
							subject.Tags[refTag.Key] = refTag.Value;
							subject.Tags[Static.maineE911id + ":" + refTag.Key] = "changed from: " + subValue;
							changed = true;
						}
						else if (TagTree.Keys.ContainsKey(refTag.Key) &&
							TagTree.Keys[refTag.Key].IsDecendantOf(subValue, refTag.Value))
						{
							// Nothing, it is already more specific.
						}
						else
						{
							conflicts.Add(Identify(refTag.Key, subject, reference));
						}
					}
				}
				else
				{
					changed = true;
					subject.Tags[refTag.Key] = refTag.Value;
					subject.Tags[Static.maineE911id + ":" + refTag.Key] = "added";
				}
			}

			if (conflicts.Any())
			{
				throw new Exception(string.Join(";", conflicts));
			}

			return changed;
		}
	}
}
