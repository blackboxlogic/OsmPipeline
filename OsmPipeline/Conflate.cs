using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using OsmSharp.Tags;

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
		private const string IgnoreListKey = Static.maineE911id + ":ignore";

		private static ILogger Log;

		public static OsmChange Merge(Osm reference, Osm subject, string scopeName, GeoJsonAPISource.Municipality municipality)
		{
			Log = Log ?? Static.LogFactory.CreateLogger(typeof(Conflate));
			// Can blacklist subject elements also! Fixme: colisions between different element types with the same ID
			var subjectElements = subject.GetElements().Where(e => !municipality.BlackList.Contains(e.Id.Value));
			var subjectElementIndex = new ElementIndex(subjectElements.ToArray());
			List<OsmGeo> create = new List<OsmGeo>(reference.Nodes);
			List<OsmGeo> modify = new List<OsmGeo>();
			List<OsmGeo> delete = new List<OsmGeo>();

			Log.LogInformation("Starting conflation, matching by tags");
			MergeNodesByTags(new[] { "addr:street", "addr:housenumber", "addr:unit" }, subjectElementIndex, municipality.WhiteList, create, modify);
			MergeNodesByTags(new[] { "addr:street", "addr:housenumber" }, subjectElementIndex, municipality.WhiteList, create, modify);
			MergeNodesByTags(new[] { "name" }, subjectElementIndex, municipality.WhiteList, create, modify);

			Log.LogInformation("Starting conflation, matching by geometry");
			MergeNodesByGeometry(subjectElementIndex, municipality.WhiteList, create, modify);

			ValidateNamesMatch(subjectElementIndex, create.Concat(modify), "highway", "addr:street",
				(element, key) => ShouldStreetBeAPlace(element, subjectElementIndex));
			ValidateNamesMatch(subjectElementIndex, create.Concat(modify), "place", "addr:place");

			Log.LogInformation($"Writing {scopeName} review files");
			var review = GatherExceptions(municipality.WhiteList, municipality.IgnoreList, subjectElementIndex, create, delete, modify);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Review.osm", review, subjectElementIndex);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Create.osm", create, subjectElementIndex);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Delete.osm", delete, subjectElementIndex);
			WriteToFileWithChildrenIfAny(scopeName + "/Conflated.Modify.osm", modify, subjectElementIndex);

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
				foreach (var element in review.GetElements().Where(e => e.Tags != null))
				{
					foreach (var revTag in element.Tags.Where(t => t.Key == WarnKey || t.Key == ErrorKey))
					{
						Console.WriteLine(Identify(element) + " " + revTag);
					}
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
				ExcuseListedElements(errors, whiteList, WhiteListKey);
				review.AddRange(errors);
				var warns = elements.Where(e => e.Tags.ContainsKey(WarnKey)).ToList();
				ExcuseListedElements(warns, whiteList, WhiteListKey);
				review.AddRange(warns);
				elements.RemoveAll(errors.Contains);
				ExcuseListedElements(elements.ToList(), whiteList, WhiteListKey); // doesn't remove, just to add maineE911id:whitelist=yes
				var distinct = elements.Distinct().ToArray();
				elements.Clear();
				elements.AddRange(distinct);
			}

			ExcuseListedElements(review, ignoreList, IgnoreListKey);
			AddFixMe(review);
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

		private static void AddFixMe(IEnumerable<OsmGeo> elements)
		{
			foreach (var element in elements)
			{
				var warning = element.Tags[WarnKey].Replace(Identify(element), "E911id-" + (-element.Id));
				if (element.Tags.TryGetValue(ReviewWithKey, out string reviewWith))
				{
					warning += ". See " + reviewWith;
				}
				element.Tags.AddOrAppend("fixme", warning);
			}
		}

		private static void ExcuseListedElements(ICollection<OsmGeo> elements, List<long> excuseList, string keyToFlagYes)
		{
			foreach (var element in elements.Where(e => IsOnTheList(e, excuseList)).ToArray())
			{
				element.Tags.AddOrAppend(keyToFlagYes, "yes");
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
			Action<OsmGeo, string> whenMissing = null)
		{
			whenMissing = whenMissing
				?? ((element, key) => element.Tags.AddOrAppend(InfoKey, "Cannot find a matching " + key));

			var parentNames = subjectElementIndex.Elements
				.Where(w => w.Tags != null && w.Tags.ContainsKey(parentFilterKey))
				.SelectMany(w => Tags.GetNames(w.Tags))
				.ToHashSet();

			var parentNamesMinusPunctuation = parentNames
				.GroupBy(s => Tags.WithoutPunctuation(s), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

			var elementsWithKeyChanges = elements.Where(e => e.Tags.ContainsKey(childKey)
				&& (e.Tags.ContainsKey(Static.maineE911id) // added element
					|| e.Tags.ContainsKey(Static.maineE911id + ":" + childKey))); // changed element

			foreach (var element in elementsWithKeyChanges)
			{
				var childName = element.Tags[childKey];
				if (!parentNames.Contains(childName))
				{
					if (parentNamesMinusPunctuation.TryGetValue(childName, out string[] withPunctuationAndCase)
						&& !withPunctuationAndCase.Contains(childName))
					{
						if (withPunctuationAndCase.Length > 1)
						{
							element.Tags.AddOrAppend(WarnKey, $"Couldn't fix {childKey}={childName}, precidents conflict: {string.Join(", ", withPunctuationAndCase)}");
						}
						else
						{
							var fixedName = withPunctuationAndCase.Single();
							element.Tags[childKey] = fixedName;
							var fixType = Tags.HasPunctuation(fixedName) ? "punctuation" : "capitalization";
							element.Tags.AddOrAppend(Static.maineE911id + ":" + childKey, $"{fixType} fixed based on precident from {childName}");
						}
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
			var searches = new[] {
				new TagsCollection(
					new Tag("place", "*"),
					new Tag("name", element.Tags["addr:street"])),
				new TagsCollection(
					new Tag("waterway", "*"),
					new Tag("name", element.Tags["addr:street"]))
			};
			foreach (var search in searches)
			{
				if (subjectElementIndex.TryGetMatchingElements(search, out OsmGeo[] elements))
				{
					var name = elements.Select(e => e.Tags["name"]).Distinct().Single();
					element.Tags.Add("addr:place", name);
					element.Tags.RemoveKey("addr:street");
					element.Tags.AddOrAppend(InfoKey, $"Changed addr:street to addr:place by context");
					return;
				}
				else if (element.Tags["addr:street"].EndsWith("Island"))
				{
					element.Tags.AddOrAppend(WarnKey, "This should probably use addr:place instead of addr:street");
				}
			}

			element.Tags.AddOrAppend(InfoKey, "Cannot find a matching addr:street");
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

		private static void MergeNodesByTags(string[] searchKeys, ElementIndex subjectElementIndex,
			List<long> whiteList, List<OsmGeo> create, List<OsmGeo> modify)
		{
			var candidateReferenceElements = create.Where(e => searchKeys.All(sk => e.Tags.ContainsKey(sk))).Cast<Node>().ToArray();

			foreach (var referenceElement in candidateReferenceElements)
			{
				var searchTags = referenceElement.Tags.KeepKeysOf(searchKeys);

				if (subjectElementIndex.TryGetMatchingElements(searchTags, out var targetSubjectElements))
				{
					var matchDistances = targetSubjectElements
						.Select(element => new { element, complete = element.AsComplete(subjectElementIndex.ByOsmGeoKey) })
						.Select(match => new { match.element, match.complete, distance = Geometry.DistanceMeters(referenceElement, match.complete) })
						// Remove elements in neighbor towns
						.Where(match => !ProbablyInAnotherCity(match.element, referenceElement, match.distance))
						.OrderBy(md => md.distance)
						.ToArray();

					if (matchDistances.Length > 1)
					{
						var matchTag = Tags.GetMostDescriptiveTag(referenceElement.Tags);
						if (matchTag.Key != null) matchDistances = matchDistances.Where(md => md.element.Tags.Contains(matchTag)).ToArray();
					}

					if (matchDistances.Length > 1)
					{
						if (matchDistances.Any(matchDistance =>
							!WouldMergeTagsHaveAnyEffect(referenceElement, matchDistance.element, whiteList.Contains(Math.Abs(referenceElement.Id.Value)))
								&& matchDistance.distance <= int.Parse(Static.Config["MatchDistanceKmMaz"])))
						{
							referenceElement.Tags.AddOrAppend(InfoKey, "Multiple matches, but didn't have anything new to add");
							create.Remove(referenceElement);
						}
						else
						{
							referenceElement.Tags.AddOrAppend(WarnKey, "Multiple matches!");
							referenceElement.Tags.AddOrAppend(ReviewWithKey, Identify(matchDistances.Select(m => m.element).ToArray()));
						}
					}
					else if (matchDistances.Length == 1)
					{
						var closestMatch = matchDistances.First();
						var subjectElement = closestMatch.element;
						referenceElement.Tags.AddOrAppend(Static.maineE911id + ":matched", string.Join(";", searchKeys) + " to " + Identify(referenceElement));

						if (closestMatch.distance > int.Parse(Static.Config["MatchDistanceKmMaz"])
							&& !Geometry.IsNodeInBuilding(referenceElement, closestMatch.complete)
							&& !whiteList.Contains(Math.Abs(referenceElement.Id.Value)))
						{
							var arrow = Geometry.GetDirectionArrow(referenceElement.AsPosition(), closestMatch.complete.AsPosition());
							referenceElement.Tags.AddOrAppend(WarnKey, $"Matched elemet is too far: {(int)closestMatch.distance} m {arrow}");
							referenceElement.Tags.AddOrAppend(ReviewWithKey, Identify(subjectElement));

							try
							{
								MergeTags(referenceElement, subjectElement,
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
			var buildings = subjectElementIndex.Elements.Where(w => Tags.IsMatchable(w))
				.Select(b => b.AsComplete(subjectElementIndex.ByOsmGeoKey))
				.ToArray();
			var newNodes = create.Cast<Node>().ToArray();
			var buildingsAndInnerNewNodes = Geometry.NodesInOrNearCompleteElements(buildings, newNodes, 30, 100,
				buildings.Where(e => e.Tags?.ContainsKey("maineE911id:matched") ?? false).Select(m => m.Id).ToHashSet());

			var oldNodes = subjectElementIndex.Elements.OfType<Node>().Where(n => n.Tags?.Any() == true).ToArray();
			var buildingsAndInnerOldNodes = Geometry.NodesInCompleteElements(buildings, oldNodes);

			foreach (var buildingAndInners in buildingsAndInnerNewNodes)
			{
				var buildingHasOldNodes = buildingsAndInnerOldNodes.TryGetValue(buildingAndInners.Key, out List<Node> oldInners);

				if (buildingAndInners.Value.Count > 1)
				{
					foreach (var node in buildingAndInners.Value)
					{
						node.Tags.AddOrAppend(InfoKey, "Multiple addresses match the same building by geometry");
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
						node.Tags.AddOrAppend(Static.maineE911id + ":matched", "geometry" + " to " + Identify(node));

						if (building.Tags.Contains("gnis:reviewed", "no") || building.Tags.ContainsKey("fixme"))
						{
							node.Tags.AddOrAppend(WarnKey, "Matched an unreliable element by geometry");
						}

						if (MergeTags(node, building, whiteList.Contains(Math.Abs(node.Id.Value))))
						{
							modify.Add(building);

							if (building is Node buildingNode)
							{
								MoveNode(node, building, subjectElementIndex, Geometry.DistanceMeters(node, buildingNode));
							}
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
			ElementIndex subjectElementIndex, double currentDistanceMeters)
		{
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

		private static bool WouldMergeTagsHaveAnyEffect(OsmGeo reference, OsmGeo subject, bool isWhiteList)
		{
			try
			{
				return MergeTags(reference,subject, isWhiteList, true);
			}
			catch (Exception)
			{
				return true;
			}
		}

		private static string[] KeysToOverrideOnMerge = new[] { "addr:city", "addr:postcode" };
		// Don't report conflicts for these.
		private static string[] KeysToIgnore = new string[] { };// new[] { "building", "addr:state" };

		private static bool MergeTags(OsmGeo reference, OsmGeo subject, bool isWhiteList, bool onlyTesting = false)
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
					var metaKey = Static.maineE911id + ":" + matchedKey;
					if (!subject.Tags.ContainsKey(matchedKey)) // absent: add
					{
						if (!KeysToIgnore.Contains(refTag.Key)) changed = true;
						subject.Tags[matchedKey] = refTag.Value;
						subject.Tags[metaKey] = "added";
					}
					else if (isMoreSpecific || isWhiteList || KeysToOverrideOnMerge.Contains(refTag.Key)) // present: update
					{
						if (!KeysToIgnore.Contains(refTag.Key)) changed = true;
						subject.Tags[metaKey] =
							matchedKey == refTag.Key
							? $"changed from: {subject.Tags[matchedKey]}"
							: $"changed by {matchedKey} from: {subject.Tags[matchedKey]}";
						subject.Tags[matchedKey] = refTag.Value;
					}
					else if (KeysToIgnore.Contains(refTag.Key)) // conflict: ignore
					{
						subject.Tags[metaKey] = $"{refTag.Value} conflicted with: {matchedKey}:{subject.Tags[matchedKey]}, but ignored";
					}
					else // conflict: flag
					{
						conflicts.Add(Identify(matchedKey, subject, reference));
					}
				}
			}

			if (changed)
			{
				conflicts.AddRange(ValidateMergedTags(subject));
			}

			if (!isWhiteList && conflicts.Any())
			{
				subject.Tags = original;
				throw new MergeConflictException(string.Join(";", conflicts));
			}
			else if (onlyTesting)
			{
				subject.Tags = original;
			}

			return changed;
		}

		private static List<string> ValidateMergedTags(OsmGeo subject)
		{
			var conflicts = new List<string>();
			if (subject.Tags.ContainsKey("highway") || Geometry.IsOpenWay(subject))
			{
				conflicts.Add("Modified a highway or open way");
			}

			if (subject.Type == OsmGeoType.Relation)
			{
				conflicts.Add("Modified a relation");
			}

			foreach (var key in Tags.PrimaryElementKeys.Keys)
			{
				if (subject.Tags.Contains(Static.maineE911id + ":" + key, "added"))
				{
					conflicts.Add($"Made {Identify(subject)} into a {key}:{subject.Tags[key]}");
				}
			}

			if (subject.Tags.Contains(Static.maineE911id + ":addr:street", "added")
				&& subject.Tags.ContainsKey("addr:place"))
			{
				conflicts.Add("Added street to a place");
			}

			if (subject.Tags.Contains(Static.maineE911id + ":addr:unit", "added") // Added a unit to something with a name
				&& subject.Tags.ContainsKey("name")
				&& !subject.Tags.Contains(Static.maineE911id + "name", "added"))
			{
				conflicts.Add("Added a unit to a place with a name");
			}

			return conflicts;
		}

		public class MergeConflictException : Exception
		{
			public MergeConflictException(string message) : base(message) { }
		}
	}
}
