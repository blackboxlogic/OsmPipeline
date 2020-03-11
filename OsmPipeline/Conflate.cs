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

			var subjectElementIndex = new ElementIndex(subject.GetElements().ToArray());
			MergeNodesByAddressTags(reference, subjectElementIndex, whitelist,
				out List<OsmGeo> create, out List<OsmGeo> modify, out List<OsmGeo> delete);
			Log.LogInformation("Starting conflation, matching by geometry");
			MergeNodesByGeometry(subjectElementIndex, whitelist, create, modify);
			ValidateNamesMatch(subjectElementIndex, create.Concat(modify), "highway", "addr:street",
				(element, key) => ShouldStreetBeAPlace(element, subjectElementIndex));
			ValidateNamesMatch(subjectElementIndex, create.Concat(modify), "place", "addr:place",
				(element, key) => element.Tags.AddOrAppend(InfoKey, "Cannot find a matching " + key));

			Log.LogInformation($"Writing {scopeName} review files");
			var review = GatherExceptions(whitelist, ignoreList, subjectElementIndex, create, delete, modify);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Review.osm", review, subjectElementIndex);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Create.osm", create, subjectElementIndex);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Delete.osm", delete, subjectElementIndex);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Modify.osm", modify, subjectElementIndex);
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
				foreach (var revTag in review.GetElements().SelectMany(e => e.Tags?.Where(t => t.Key == WarnKey || t.Key == ErrorKey) ?? new Tag[0]))
				{
					Console.WriteLine(revTag);
				}
			}
		}

		// Collections are modified by reference. Errors removed and logged. Warns just logged.
		// Whitelist suppresses warns and errors.
		private static List<OsmGeo> GatherExceptions(List<long> whiteList, List<long> ignoreList,
			ElementIndex subjectElementIndex, params List<OsmGeo>[] elementss)
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
				r => r.Tags[ReviewWithKey].Split(";").Select(rw => subjectElementIndex.ByOsmGeoKey[rw]));
			foreach (var reviewWith in reviewsWith)
			{
				foreach (var with in reviewWith.Value)
				{
					var arrow = Geometry.GetDirectionArrow(
						with.AsComplete(subjectElementIndex.ByOsmGeoKey).AsPosition(),
						reviewWith.Key.AsComplete(subjectElementIndex.ByOsmGeoKey).AsPosition());
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

		private static void ValidateNamesMatch(ElementIndex subjectElementIndex,
			IEnumerable<OsmGeo> elements, string parentFilterKey, string childKey,
			Action<OsmGeo, string> whenMissing)
		{
			var parentNames = subjectElementIndex.Elements
				.Where(w => w.Tags != null && w.Tags.ContainsKey(parentFilterKey))
				.SelectMany(w => Tags.GetNames(w.Tags))
				.ToHashSet();

			var parentNamesMinusPunctuation = parentNames
				.Where(Tags.HasPunctuation)
				.ToDictionary(Tags.WithoutPunctuation);

			var elementsWithKeyChanges = elements.Where(e => e.Tags.ContainsKey(childKey)
				&& (e.Tags.ContainsKey(Static.maineE911id) // added element
					|| e.Tags.ContainsKey(Static.maineE911id + ":" + childKey))); // changed element

			foreach (var element in elementsWithKeyChanges)
			{
				var childName = element.Tags[childKey];
				if (!parentNames.Contains(childName))
				{
					if (parentNamesMinusPunctuation.TryGetValue(childName, out string withPunctuation))
					{
						element.Tags[childKey] = withPunctuation;
						element.Tags.AddOrAppend(InfoKey, "punctuation was added to street name"); // should not be error
					}
					else
					{
						whenMissing(element, parentFilterKey);
					}
				}
			}
		}

		private static void ShouldStreetBeAPlace(OsmGeo element, ElementIndex subjectElementIndex)
		{
			var search = new TagsCollection(
				new Tag("place", "*"),
				new Tag("name", element.Tags["addr:street"]));
			if (subjectElementIndex.TryGetMatchingElements(search, out OsmGeo[] elements))
			{
				var name = elements.Select(e => e.Tags["name"]).Distinct().Single();
				element.Tags.Add("addr:place", name);
				element.Tags.RemoveKey("addr:street");
				element.Tags.AddOrAppend(InfoKey, $"Changed addr:street to addr:place by context");
			}
			else
			{
				element.Tags.AddOrAppend(InfoKey, "Cannot find a matching addr:street");
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
			ElementIndex subjectElementIndex)
		{
			if (osmGeos.Any())
				FileSerializer.WriteXml(fileName,
					osmGeos.WithChildren(subjectElementIndex.ByOsmGeoKey).Distinct().AsOsm());
			else if (File.Exists(fileName))
				File.Delete(fileName);
		}

		private static void MergeNodesByAddressTags(
			Osm reference, ElementIndex subjectElementIndex, List<long> whiteList,
			out List<OsmGeo> create, out List<OsmGeo> modify, out List<OsmGeo> delete)
		{
			create = new List<OsmGeo>(reference.Nodes);
			modify = new List<OsmGeo>();
			delete = new List<OsmGeo>();

			foreach (var referenceElement in create.Cast<Node>().ToArray())
			{
				var searchTags = referenceElement.Tags.Where(t => t.Key == "addr:street" || t.Key == "addr:housenumber");

				if (subjectElementIndex.TryGetMatchingElements(new TagsCollection(searchTags), out var targetSubjectElements))
				{
					var matchDistances = targetSubjectElements
						.Select(element => new { element, complete = element.AsComplete(subjectElementIndex.ByOsmGeoKey) })
						.Select(match => new { match.element, match.complete, distance = Geometry.DistanceMeters(referenceElement, match.complete) })
						// Remove elements in neighbor towns
						.Where(match => !ProbablyInAnotherCity(match.element, referenceElement, match.distance))
						.ToArray();

					if (matchDistances.Length > 1)
					{
						var matchTag = Tags.GetMostDescriptiveTag(referenceElement.Tags);
						if (matchTag.Key != null) matchDistances = matchDistances.Where(md => md.element.Tags.Contains(matchTag)).ToArray();
					}

					if (matchDistances.Length > 1)
					{
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

							try
							{
								bool tagsChanged = MergeTags(referenceElement, subjectElement,
									whiteList.Contains(Math.Abs(referenceElement.Id.Value)), true);
							}
							catch (MergeConflictException e)
							{
								referenceElement.Tags.AddOrAppend(WarnKey, e.Message);
								referenceElement.Tags.AddOrAppend(ReviewWithKey, Identify(subjectElement));
							}
						}
						else
						{
							try
							{
								bool tagsChanged = MergeTags(referenceElement, subjectElement,
									whiteList.Contains(Math.Abs(referenceElement.Id.Value)));
								tagsChanged |= MoveNode(referenceElement, subjectElement, subjectElementIndex, closestMatch.distance);

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
			return (a.Tags.TryGetValue("addr:city", out string aCity)
				&& b.Tags.TryGetValue("addr:city", out string bCity)
				&& !string.Equals(aCity, bCity, StringComparison.OrdinalIgnoreCase)
				&& distanceMeters > 200)
				|| distanceMeters > 2000;
		}

		private static void MergeNodesByGeometry(ElementIndex subjectElementIndex,
			List<long> whiteList, List<OsmGeo> create, List<OsmGeo> modify)
		{
			var buildings = subjectElementIndex.Elements.Where(w => Tags.IsBuilding(w))
				.Select(b => b.AsComplete(subjectElementIndex.ByOsmGeoKey))
				.ToArray();
			var newNodes = create.Cast<Node>().ToArray();
			var buildingsAndInnerNewNodes = Geometry.NodesInOrNearCompleteElements(buildings, newNodes, 30, 100);
			var oldNodes = subjectElementIndex.Elements.OfType<Node>().Where(n => n.Tags?.Any() == true).ToArray();
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
					var building = subjectElementIndex.ByOsmGeoKey[buildingAndInners.Key.Type.ToString() + buildingAndInners.Key.Id];

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
				&& (aStreet == bStreet || TagTree.Keys["addr:street"].IsDecendantOf(aStreet, bStreet))
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
			ElementIndex subjectElementIndex, double currentDistanceMeters)
		{
			//return false;

			if (subject is Node subjectNode
				&& reference is Node referenceNode
				&& currentDistanceMeters > int.Parse(Static.Config["MinNodeMoveDistance"]))
			{
				if (subjectElementIndex.Elements.OfType<Way>().Any(w => w.Nodes.Contains(subject.Id.Value)))
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

		private static bool MergeTags(OsmGeo reference, OsmGeo subject, bool isWhiteList, bool onlyTestForConflicts = false)
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
				else if (!Tags.TagMatchesTags(refTag, subject.Tags, out bool isMoreSpecific, out string matchedKey))
				{
					if (!subject.Tags.ContainsKey(matchedKey)) // absent
					{
						changed = true;
						subject.Tags[matchedKey] = refTag.Value;
						subject.Tags[Static.maineE911id + ":" + matchedKey] = "added";
					}
					else if (isMoreSpecific || isWhiteList) // compelled
					{
						changed = true;
						subject.Tags[Static.maineE911id + ":" + matchedKey] =
							matchedKey == refTag.Key
							? $"changed from: {subject.Tags[matchedKey]}"
							: $"changed by {matchedKey} from: {subject.Tags[matchedKey]}";
						subject.Tags[matchedKey] = refTag.Value;
					}
					else // conflict
					{
						conflicts.Add(Identify(matchedKey, subject, reference));
					}
				}
			}

			if (changed)
			{
				ValidateMergedTags(subject, conflicts);
			}

			if (conflicts.Any())
			{
				subject.Tags = original;
				throw new MergeConflictException(string.Join(";", conflicts));
			}
			else if (onlyTestForConflicts)
			{
				subject.Tags = original;
			}

			return changed;
		}

		private static void ValidateMergedTags(OsmGeo subject, List<string> conflicts)
		{
			if (subject.Tags.ContainsKey("highway") || Geometry.IsOpenWay(subject))
			{
				conflicts.Add("Modified a highway or open way");
			}

			if (subject.Tags.Contains(Static.maineE911id + ":building", "added") && !(subject is Node))
			{
				conflicts.Add($"Made a {subject.Type} into a building");
			}

			if (subject.Tags.Contains(Static.maineE911id + ":addr:street", "added")
				&& subject.Tags.ContainsKey("addr:place"))
			{
				conflicts.Add("Added street to a place");
			}
		}

		public class MergeConflictException : Exception
		{
			public MergeConflictException(string message) : base(message) { }
		}
	}
}
