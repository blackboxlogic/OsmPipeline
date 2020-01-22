using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Linq;
using System.Collections.Generic;
using OsmSharp;
using Microsoft.Extensions.Logging;
using OsmPipeline.Fittings;
using OsmSharp.Tags;
using System.IO;

namespace OsmPipeline
{
	public static class Conflate
	{
		private const string ErrorKey = Static.maineE911id + ":error";
		private const string WarnKey = Static.maineE911id + ":warn";
		private const string MovedKey = Static.maineE911id + ":moved";
		private const string InfoKey = Static.maineE911id + ":info";
		private const string WhiteListKey = Static.maineE911id + ":whitelist";

		private static ILogger Log;

		public static OsmChange Merge(Osm reference, Osm subject, string scopeName)
		{
			Log = Log ?? Static.LogFactory.CreateLogger(typeof(Conflate));
			Log.LogInformation("Starting conflation, matching by address tags");

			var subjectElements = new OsmGeo[][] { subject.Nodes, subject.Ways, subject.Relations }.SelectMany(e => e);
			var subjectElementsIndexed = subjectElements.ToDictionary(n => n.Type.ToString() + n.Id);
			MergeNodesByAddressTags(reference, subjectElementsIndexed, out List<OsmGeo> create, out List<OsmGeo> modify, out List<OsmGeo> delete);
			ValidateRoadNamesMatcheRoads(subjectElementsIndexed, create);
			Log.LogInformation("Starting conflation, matching by geometry");
			MergeNodesByGeometry(subjectElementsIndexed, create, modify);
			Log.LogInformation($"Writing {scopeName} review files");
			var exceptions = GatherExceptions(scopeName, create, delete, modify);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Review.osm", exceptions, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Create.osm", create, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Delete.osm", delete, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Modify.osm", modify, subjectElementsIndexed);
			CheckForOffset(modify);
			RemoveReviewTags(create, delete, modify);
			var change = Translate.GeosToChange(create, modify, delete, "OsmPipeline");
			LogSummary(change, exceptions);

			return change;
		}

		// Collections are modified by reference. Errors removed and logged. Warns just logged.
		// Whitelist suppresses warns and errors.
		private static List<OsmGeo> GatherExceptions(string scopeName, params List<OsmGeo>[] elementss)
		{
			var exceptions = new List<OsmGeo>();

			foreach (var elements in elementss)
			{
				var errors = elements.Where(e => e.Tags.ContainsKey(ErrorKey)).ToHashSet();
				ExcuseWhitelistedElements(errors, scopeName);
				exceptions.AddRange(errors);
				var warns = elements.Where(e => e.Tags.ContainsKey(WarnKey)).ToHashSet();
				ExcuseWhitelistedElements(warns, scopeName);
				exceptions.AddRange(warns);
				elements.RemoveAll(errors.Contains);
			}

			return exceptions;
		}

		private static void ExcuseWhitelistedElements(ICollection<OsmGeo> elements, string scopeName)
		{
			var whitelist = Static.Municipalities[scopeName].WhiteList.Select(Math.Abs).ToHashSet();
			foreach (var error in elements.Where(e => whitelist.Contains(-e.Id.Value)
					|| e.Tags[Static.maineE911id].Split(';').All(id => whitelist.Contains(long.Parse(id)))).ToArray())
			{
				error.Tags.AddOrAppend(WhiteListKey, "yes");
				elements.Remove(error);
			}
		}

		private static void ValidateRoadNamesMatcheRoads(Dictionary<string, OsmGeo> subjectElementsIndexed, List<OsmGeo> create)
		{
			var roadNames = subjectElementsIndexed.Values
				.Where(w => w.Tags != null && w.Tags.ContainsKey("highway") && w.Tags.ContainsKey("name"))
				.SelectMany(w => w.Tags)
				.Where(t => t.Key.Contains("name"))
				.Select(t => t.Value)
				.ToHashSet();

			foreach (var created in create)
			{
				if (!roadNames.Contains(created.Tags["addr:street"]))
				{
					created.Tags.AddOrAppend(InfoKey, "Cannot find a matching highway");
				}
			}
		}

		private static void CheckForOffset(IList<OsmGeo> modify)
		{
			// Try to detect if the data is geographically shifted
			var arrowsSummary = modify
				.Where(m => m.Tags.ContainsKey(MovedKey))
				.Select(m => m.Tags[MovedKey].Last())
				.GroupBy(c => c)
				.ToDictionary(c => c.Key, c => c.Count());

			if (arrowsSummary.Values.DefaultIfEmpty().Max() + 1 > 10 * arrowsSummary.Values.DefaultIfEmpty().Min() + 1)
			{
				Log.LogWarning("There might be an offset!");
			}
		}

		private static void LogSummary(OsmChange change, IList<OsmGeo> exceptions)
		{
			Log.LogInformation($"{nameof(change.Create)}: {change.Create.Length}" +
				$"\n\t{nameof(change.Modify)}: {change.Modify.Length}" +
				$"\n\t{nameof(change.Delete)}: {change.Delete.Length}" +
				$"\n\t{nameof(exceptions)}: {exceptions.Count}");
		}

		private static void RemoveReviewTags(params IList<OsmGeo>[] elements)
		{
			foreach (var element in elements.SelectMany(e => e))
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
			else if (File.Exists(fileName))
				File.Delete(fileName);
		}

		private static void MergeNodesByAddressTags(Osm reference, Dictionary<string, OsmGeo> subjectElementsIndexed, out List<OsmGeo> create,
			out List<OsmGeo> modify, out List<OsmGeo> delete)
		{
			create = new List<OsmGeo>(reference.Nodes);
			modify = new List<OsmGeo>();
			delete = new List<OsmGeo>();

			var subjectAddressIndex = subjectElementsIndexed.Values
				.GroupBy(e => Tags.GetAddrTags(e, false))
				.Where(g => g.Key.Any())
				.ToDictionary(g => g.Key, g => g.ToArray());

			foreach (var referenceElement in create.OfType<Node>().ToArray())
			{
				var addr = referenceElement.GetAddrTags();

				if (!referenceElement.Tags.ContainsKey("addr:housenumber"))
				{
					referenceElement.Tags.AddOrAppend(ErrorKey, "Missing house number!");
				}
				else if (subjectAddressIndex.TryGetValue(addr, out var targetSubjectElements))
				{
					if (targetSubjectElements.Length > 1)
					{
						// TODO: Resolve these multi-matches by checking tag conflicts (or if IN ONE of them)
						referenceElement.Tags.AddOrAppend(ErrorKey, "Multiple matches!" + Identify(targetSubjectElements));
					}
					else
					{
						var refCentroid = Geometry.AsPosition(referenceElement.AsComplete(subjectElementsIndexed));
						var closestMatch = targetSubjectElements
							.Select(element => new { element, centroid = Geometry.AsPosition(element.AsComplete(subjectElementsIndexed)) })
							.Select(match => new { match.element, distance = Geometry.DistanceMeters(refCentroid, match.centroid) })
							.OrderBy(match => match.distance).First();
						var subjectElement = closestMatch.element;
						var completeSubjectElement = subjectElement.AsComplete(subjectElementsIndexed);
						if (closestMatch.distance > int.Parse(Static.Config["MatchDistanceKmMaz"])
							&& !Geometry.IsNodeInBuilding(referenceElement, completeSubjectElement))
						{
							var arrow = Geometry.GetDirectionArrow(referenceElement.AsPosition(), completeSubjectElement.AsPosition());
							referenceElement.Tags.AddOrAppend(WarnKey, $"Matched, but too far: {(int)closestMatch.distance} meters {arrow}.{Identify(subjectElement)}");
						}
						else
						{
							try
							{
								bool tagsChanged = MergeTags(referenceElement, subjectElement);
								tagsChanged |= MoveNode(referenceElement, subjectElement, closestMatch.distance);

								if (tagsChanged)
								{
									if (modify.Any(n => n.Id == subjectElement.Id))
										subjectElement.Tags.AddOrAppend(WarnKey, "Subject modified by multiple references");
									modify.Add(subjectElement);
								}
								create.Remove(referenceElement);
							}
							catch (MergeConflictException e)
							{
								referenceElement.Tags.AddOrAppend(WarnKey, e.Message);
							}
						}
					}
				}
			}
		}

		private static void MergeNodesByGeometry(Dictionary<string, OsmGeo> subjectElementsIndexed,
			List<OsmGeo> create, List<OsmGeo> modify)
		{
			var buildings = subjectElementsIndexed.Values.Where(w => Tags.IsBuilding(w))
				.Select(b => b.AsComplete(subjectElementsIndexed))
				.ToArray();
			var newNodes = create.OfType<Node>().ToArray();
			var buildingsAndInnerNewNodes = Geometry.NodesInCompleteElements(buildings, newNodes);

			var oldNodes = subjectElementsIndexed.Values.OfType<Node>().ToArray();
			var buildingsAndInnerOldNodes = Geometry.NodesInCompleteElements(buildings, oldNodes);

			foreach (var buildingAndInners in buildingsAndInnerNewNodes)
			{
				var buildingHasOldNodes = buildingsAndInnerOldNodes.ContainsKey(buildingAndInners.Key);

				if (buildingAndInners.Value.Length > 1)
				{
					foreach (var node in buildingAndInners.Value)
					{
						node.Tags.AddOrAppend(InfoKey, "Multiple addresses land in the same building");
						if (buildingHasOldNodes)
						{
							node.Tags.AddOrAppend(WarnKey, "New node in building with old nodes");
						}
					}
				}
				else
				{
					var node = buildingAndInners.Value.First();
					var building = subjectElementsIndexed[buildingAndInners.Key.Type.ToString() + buildingAndInners.Key.Id];

					try
					{
						if (MergeTags(node, building))
						{
							modify.Add(building);
						}
						create.Remove(node);
					}
					catch (MergeConflictException e)
					{
						node.Tags.AddOrAppend(WarnKey, e.Message); // or info?
						if(buildingHasOldNodes)
						{
							node.Tags.AddOrAppend(WarnKey, "New node in building with old nodes");
						}
					}
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
				var arrow = Geometry.GetDirectionArrow(subjectNode.AsPosition(), referenceNode.AsPosition());
				subjectNode.Latitude = referenceNode.Latitude;
				subjectNode.Longitude = referenceNode.Longitude;
				// Mark it for easier review
				subject.Tags.AddOrReplace(MovedKey, (int)currentDistanceMeters + " meters " + arrow);
				return true;
			}

			return false;
		}

		private static bool MergeTags(OsmGeo reference, OsmGeo subject)
		{
			var original = new TagsCollection(subject.Tags); // Deep Clone
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
				subject.Tags = original;
				throw new MergeConflictException(string.Join(";", conflicts));
			}

			return changed;
		}

		public class MergeConflictException : Exception
		{
			public MergeConflictException(string message) : base(message) { }
		}
	}
}
