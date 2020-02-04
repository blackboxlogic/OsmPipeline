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
		private const string ReviewWithKey = Static.maineE911id + ":review-with";
		private const string WhiteListKey = Static.maineE911id + ":whitelist";

		private static ILogger Log;

		public static OsmChange Merge(Osm reference, Osm subject, string scopeName, List<long> whitelist = null, List<long> ignoreList = null)
		{
			whitelist = whitelist ?? new List<long>();
			ignoreList = ignoreList ?? new List<long>();
			Log = Log ?? Static.LogFactory.CreateLogger(typeof(Conflate));
			Log.LogInformation("Starting conflation, matching by address tags");

			var subjectElementsIndexed = subject.OsmToGeos().ToDictionary(n => n.Type.ToString() + n.Id); // could use OsmGeoKey
			MergeNodesByAddressTags(reference, subjectElementsIndexed, whitelist,
				out List<OsmGeo> create, out List<OsmGeo> modify, out List<OsmGeo> delete);
			ValidateRoadNamesMatcheRoads(subjectElementsIndexed, create);
			Log.LogInformation("Starting conflation, matching by geometry");
			MergeNodesByGeometry(subjectElementsIndexed, whitelist, create, modify);
			Log.LogInformation($"Writing {scopeName} review files");
			var review = GatherExceptions(whitelist, ignoreList, subjectElementsIndexed, create, delete, modify);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Review.osm", review, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Create.osm", create, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Delete.osm", delete, subjectElementsIndexed);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Modify.osm", modify, subjectElementsIndexed);
			CheckForOffset(modify);
			RemoveReviewTags(create, delete, modify);
			var change = Translate.GeosToChange(create, modify, delete, "OsmPipeline");
			LogSummary(change, review);

			return change;
		}

		public static void Review(string scopeName)
		{
			var review = FileSerializer.ReadXml<Osm>(scopeName + "/Conflated.Review.osm");
			if (review != null)
			{
				foreach (var revTag in review.OsmToGeos().SelectMany(e => e.Tags?.Where(t => t.Key == WarnKey || t.Key == ErrorKey) ?? new Tag[0]))
				{
					Console.WriteLine(revTag);
				}
			}
		}

		// Collections are modified by reference. Errors removed and logged. Warns just logged.
		// Whitelist suppresses warns and errors.
		private static List<OsmGeo> GatherExceptions(List<long> whiteList, List<long> ignoreList,
			Dictionary<string, OsmGeo> subjectElementsIndexed, params List<OsmGeo>[] elementss)
		{
			var review = new List<OsmGeo>();

			foreach (var elements in elementss)
			{
				var errors = elements.Where(e => e.Tags.ContainsKey(ErrorKey)).ToList();
				ExcuseWhitelistedElements(errors, whiteList);
				review.AddRange(errors);
				var warns = elements.Where(e => e.Tags.ContainsKey(WarnKey)).ToList();
				ExcuseWhitelistedElements(warns, whiteList);
				review.AddRange(warns);
				elements.RemoveAll(errors.Contains);
				ExcuseWhitelistedElements(elements.ToList(), whiteList); // doesn't remove, just to add maineE911id:whitelist=yes
				var distinct = elements.Distinct().ToArray();
				elements.Clear();
				elements.AddRange(distinct);
			}

			review.RemoveAll(r => IsOnTheList(r, ignoreList));
			var reviewsWith = review.ToArray().Where(e => e.Tags.ContainsKey(ReviewWithKey)).Distinct()
				.ToDictionary(r => r,
				r => r.Tags[ReviewWithKey].Split(";").Select(rw => subjectElementsIndexed[rw]));
			foreach (var reviewWith in reviewsWith)
			{
				foreach (var with in reviewWith.Value)
				{
					var arrow = Geometry.GetDirectionArrow(
						with.AsComplete(subjectElementsIndexed).AsPosition(),
						reviewWith.Key.AsComplete(subjectElementsIndexed).AsPosition());
					with.Tags.AddOrAppend(InfoKey, arrow.ToString());
					with.Tags.AddOrAppend(Static.maineE911id + ":osm-id", with.Type + "/" + with.Id);
					review.Add(with);
				}
			}

			return review;
		}

		private static void ExcuseWhitelistedElements(ICollection<OsmGeo> elements, List<long> whitelist)
		{
			foreach (var element in elements.Where(e => IsOnTheList(e, whitelist)).ToArray())
			{
				element.Tags.AddOrAppend(WhiteListKey, "yes");
				elements.Remove(element);
			}
		}

		private static bool IsOnTheList(OsmGeo element, List<long> list)
		{
			return list.Contains(Math.Abs(element.Id.Value))
				|| (element.Tags.ContainsKey(Static.maineE911id)
					&& element.Tags[Static.maineE911id].Split(';').All(id => list.Contains(long.Parse(id))));
		}

		private static void ValidateRoadNamesMatcheRoads(Dictionary<string, OsmGeo> subjectElementsIndexed, List<OsmGeo> create)
		{
			var roadNames = subjectElementsIndexed.Values
				.Where(w => w.Tags != null && w.Tags.ContainsKey("highway") && w.Tags.ContainsKey("name")) // should expand this... official_name, ref etc
				.SelectMany(w => w.Tags)
				.Where(t => t.Key.Contains("name"))
				.Select(t => t.Value)
				.ToHashSet();

			foreach (var created in create)
			{
				if (created.Tags.ContainsKey("addr:street") && !roadNames.Contains(created.Tags["addr:street"]))
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

		private static void LogSummary(OsmChange change, IList<OsmGeo> Review)
		{
			Log.LogInformation($"{nameof(change.Create)}: {change.Create.Length}" +
				$"\n\t{nameof(change.Modify)}: {change.Modify.Length}" +
				$"\n\t{nameof(change.Delete)}: {change.Delete.Length}" +
				$"\n\t{nameof(Review)}: {Review.Count}");
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

		private static void MergeNodesByAddressTags(
			Osm reference, Dictionary<string, OsmGeo> subjectElementsIndexed, List<long> whiteList,
			out List<OsmGeo> create, out List<OsmGeo> modify, out List<OsmGeo> delete)
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
					var matchDistances = targetSubjectElements
						.Select(element => new { element, complete = element.AsComplete(subjectElementsIndexed) })
						.Select(match => new { match.element, match.complete, distance = Geometry.DistanceMeters(referenceElement, match.complete) })
						// Remove elements in other towns
						.Where(match => !ProbablyInAnotherCity(match.element, referenceElement, match.distance))
						.ToArray();

					if (matchDistances.Length > 1)
					{
						// TODO: Resolve these multi-matches by checking tag conflicts
						referenceElement.Tags.AddOrAppend(ErrorKey, "Multiple matches!");
						referenceElement.Tags.AddOrAppend(ReviewWithKey, Identify(matchDistances.Select(m => m.element).ToArray()));
					}
					else if (matchDistances.Length == 1)
					{
						var closestMatch = matchDistances.OrderBy(match => match.distance).First();
						var subjectElement = closestMatch.element;

						if (closestMatch.distance > int.Parse(Static.Config["MatchDistanceKmMaz"])
							&& !Geometry.IsNodeInBuilding(referenceElement, closestMatch.complete)
							&& !whiteList.Contains(Math.Abs(referenceElement.Id.Value)))
						{
							var arrow = Geometry.GetDirectionArrow(referenceElement.AsPosition(), closestMatch.complete.AsPosition());
							referenceElement.Tags.AddOrAppend(WarnKey, $"Matched, but too far: {(int)closestMatch.distance} m {arrow}");
							referenceElement.Tags.AddOrAppend(ReviewWithKey, Identify(subjectElement));
						}
						else
						{
							try
							{
								bool tagsChanged = MergeTags(referenceElement, subjectElement,
									whiteList.Contains(Math.Abs(referenceElement.Id.Value)));
								tagsChanged |= MoveNode(referenceElement, subjectElement, subjectElementsIndexed, closestMatch.distance);

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
								referenceElement.Tags.AddOrAppend(ReviewWithKey, Identify(subjectElement));
							}
						}
					}
				}
			}
		}

		private static bool ProbablyInAnotherCity(OsmGeo a, OsmGeo b, double distanceMeters)
		{
			return a.Tags.TryGetValue("addr:city", out string aCity)
				&& b.Tags.TryGetValue("addr:city", out string bCity)
				&& !string.Equals(aCity, bCity, StringComparison.OrdinalIgnoreCase)
				&& distanceMeters > 200;
		}

		private static void MergeNodesByGeometry(Dictionary<string, OsmGeo> subjectElementsIndexed,
			List<long> whiteList, List<OsmGeo> create, List<OsmGeo> modify)
		{
			var buildings = subjectElementsIndexed.Values.Where(w => Tags.IsBuilding(w))
				.Select(b => b.AsComplete(subjectElementsIndexed))
				.ToArray();
			var newNodes = create.OfType<Node>().ToArray();
			var buildingsAndInnerNewNodes = Geometry.NodesInOrNearCompleteElements(buildings, newNodes, 30, 100);
			var oldNodes = subjectElementsIndexed.Values.OfType<Node>().Where(n => n.Tags?.Any() == true).ToArray();
			var buildingsAndInnerOldNodes = Geometry.NodesInCompleteElements(buildings, oldNodes);

			foreach (var buildingAndInners in buildingsAndInnerNewNodes)
			{
				var buildingHasOldNodes = buildingsAndInnerOldNodes.TryGetValue(buildingAndInners.Key, out Node[] oldInners);

				if (buildingAndInners.Value.Length > 1)
				{
					foreach (var node in buildingAndInners.Value)
					{
						node.Tags.AddOrAppend(InfoKey, "Multiple addresses land in the same building");
						node.Tags.AddOrAppend(ReviewWithKey, buildingAndInners.Key.Type.ToString() + buildingAndInners.Key.Id);
						if (buildingHasOldNodes)
						{
							node.Tags.AddOrAppend(InfoKey, "New node in building with old nodes");
							node.Tags.AddOrAppend(ReviewWithKey, oldInners.Select(i => i.Type.ToString() + i.Id).ToArray());
						}
					}
				}
				else
				{
					var node = buildingAndInners.Value.First();
					var building = subjectElementsIndexed[buildingAndInners.Key.Type.ToString() + buildingAndInners.Key.Id];

					try
					{
						if (MergeTags(node, building, whiteList.Contains(Math.Abs(node.Id.Value))))
						{
							modify.Add(building);
						}
						create.Remove(node);
					}
					catch (MergeConflictException e)
					{
						if (LikelyNeighbors(node, building))
						{
							node.Tags.AddOrAppend(InfoKey, "Assummed to be a multi-address building");
						}
						else
						{
							node.Tags.AddOrAppend(WarnKey, e.Message);
							node.Tags.AddOrAppend(ReviewWithKey, building.Type.ToString() + building.Id);
							if (buildingHasOldNodes)
							{
								node.Tags.AddOrAppend(InfoKey, "New node in building with old nodes");
								node.Tags.AddOrAppend(ReviewWithKey, oldInners.Select(i => i.Type.ToString() + i.Id).ToArray());
							}
						}
					}
				}
			}
		}

		private static bool LikelyNeighbors(OsmGeo a, OsmGeo b)
		{
			return a.Tags != null && b.Tags != null
				&& a.Tags.TryGetValue("addr:street", out string aStreet)
				&& b.Tags.TryGetValue("addr:street", out string bStreet)
				&& aStreet == bStreet
				&& a.Tags.TryGetValue("addr:housenumber", out string aNum)
				&& b.Tags.TryGetValue("addr:housenumber", out string bNum)
				&& int.TryParse(aNum, out int aInt)
				&& int.TryParse(bNum, out int bInt)
				&& (Math.Abs(aInt - bInt) == 2
					|| Math.Abs(aInt - bInt) == 4);
		}

		private static string Identify(params OsmGeo[] elements)
		{
			return string.Join(";", elements.Select(e => $"{e.Type.ToString()}{e.Id}"));
		}

		private static string Identify(string key, params OsmGeo[] elements)
		{
			return string.Join(";", elements.Select(e => $"{e.Type.ToString()}{e.Id}.{key}={e.Tags[key]}"));
		}

		private static bool MoveNode(OsmGeo reference, OsmGeo subject,
			Dictionary<string, OsmGeo> subjectElementsIndexed, double currentDistanceMeters)
		{
			if (subject is Node subjectNode
				&& reference is Node referenceNode
				&& currentDistanceMeters > int.Parse(Static.Config["MinNodeMoveDistance"]))
			{
				if (subjectElementsIndexed.Values.OfType<Way>().Any(w => w.Nodes.Contains(subject.Id.Value)))
				{
					subject.Tags.AddOrAppend(WarnKey, "Chose not to move node because it is part of a way");
					return false;
				}

				var arrow = Geometry.GetDirectionArrow(subjectNode.AsPosition(), referenceNode.AsPosition());
				subjectNode.Latitude = referenceNode.Latitude;
				subjectNode.Longitude = referenceNode.Longitude;
				// Mark it for easier review
				subject.Tags.AddOrReplace(MovedKey, (int)currentDistanceMeters + " m " + arrow);
				return true;
			}

			return false;
		}

		private static bool MergeTags(OsmGeo reference, OsmGeo subject, bool isWhiteList)
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
					if (subValue != refTag.Value && !subValue.Split(";-,:".ToArray()).Contains(refTag.Value))
					{
						if (TagTree.Keys.ContainsKey(refTag.Key) &&
							TagTree.Keys[refTag.Key].IsDecendantOf(refTag.Value, subValue)
							|| isWhiteList)
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

					if (refTag.Key == "building" && (subject is Relation || subject is Way subWay && subWay.Nodes.Length > 10))
					{
						subject.Tags.AddOrAppend(ErrorKey, "Made this a building");
					}

					if (subject is Way subWay2 && subWay2.Nodes.First() != subWay2.Nodes.Last())
					{
						subject.Tags.AddOrAppend(ErrorKey, "Modified an open way");
					}
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
