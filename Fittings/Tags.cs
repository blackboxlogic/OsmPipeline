﻿using OsmSharp;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Tags
	{
		public static readonly Dictionary<string, string> AlternateKeys = new Dictionary<string, string>()
		{
			{ "addr:street", "addr:place" },
			{ "building", "amenity" },
			{ "name", "alt_name" } // new [] { "alt_name", "official_name", "old_name", "local_name", "ref", "name:left", "name:right", "name_1" } }
		};

		public static readonly Dictionary<string, Func<string, string[]>> MatchableTagKeys =
			new Dictionary<string, Func<string, string[]>>()
			{
				{ "addr:street", Tags.WithOrWithoutPunctuation },
				{ "addr:place", Tags.WithOrWithoutPunctuation },
				{ "addr:housenumber", Tags.GetNumbersFromRangeOrList },
				{ "name", Tags.WithOrWithoutPunctuation }
			};

		public static Func<string, string[]> GetMatchingFunction(string key)
		{
			return MatchableTagKeys.TryGetValue(key, out var value) ? value : GetPartsFromList;
		}

		private static string[] GetPartsFromList(string value)
		{
			return value.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
		}

		private static string[] GetNumbersFromRangeOrList(string value)
		{
			if (!HasPunctuation(value)) return new[] { value };

			var numbers = new List<string>();
			var parts = GetPartsFromList(value);
			foreach (var part in parts)
			{
				if (part.Contains('-'))
				{
					var range = part.Split('-');
					if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end) && start < end)
					{
						var count = end - start + 1; // + 1 because inclusive
						numbers.AddRange(Enumerable.Range(start, count).Select(n => n.ToString()));
					}
				}
				else
				{
					numbers.Add(part.Trim());
				}
			}

			return numbers.ToArray();
		}

		public static bool HasPunctuation(string value)
		{
			return value.Any(char.IsPunctuation);
		}

		public static string[] WithOrWithoutPunctuation(string value)
		{
			return HasPunctuation(value)
				? new string[] { value, new string(value.Where(c => !char.IsPunctuation(c)).ToArray()) }
				: new string[] { value };
		}

		public static string WithoutPunctuation(string value)
		{
			return new string(value.Where(c => !char.IsPunctuation(c)).ToArray());
		}

		public static void AddOrAppend(this TagsCollectionBase tags, string key, params string[] newValues)
		{
			if (tags.TryGetValue(key, out var oldValue))
			{
				foreach (var newValue in newValues.Where(n => !oldValue.Contains(n)))
				{
					tags[key] += ";" + newValue;
				}
			}
			else
			{
				tags.Add(key, string.Join(";", newValues));
			}
		}

		public static bool IsAddressy(OsmGeo element)
		{
			return element.Tags?.Any(t => t.Key.StartsWith("addr:")) == true;
		}

		public static bool IsBuilding(OsmGeo element)
		{
			return element.Tags?.ContainsKey("building") == true;
		}

		public static TagsCollectionBase GetBaseAddress(Node node)
		{
			var addressTags = new[] { "addr:housenumber", "addr:street", "addr:city", "addr:state" };
			return node.Tags.KeepKeysOf(addressTags);
		}

		public static IEnumerable<string> GetNames(TagsCollectionBase tags)
		{
			return tags.Where(t => t.Key == "ref" || t.Key.Split('_', ':').Contains("name")).Select(t => t.Value);
		}

		public static bool TagMatchesTags(Tag tag, TagsCollectionBase tags, out bool isMoreSpecific, out string key)
		{
			// Return true if is less specific!
			isMoreSpecific = false;
			key = tag.Key;
			return (tags.ContainsKey(key) && ValuesMatch(key, tags[key], tag.Value, out isMoreSpecific))
				|| (AlternateKeys.TryGetValue(key, out key)
					&& tags.ContainsKey(key)
					&& ValuesMatch(tag.Key, tags[key], tag.Value, out isMoreSpecific));
		}

		private static bool ValuesMatch(string key, string parent, string suspectedchild, out bool isMoreSpecific)
		{
			var matcher = GetMatchingFunction(key);
			var parentVariations = matcher(parent);
			var childVariations = matcher(suspectedchild);

			isMoreSpecific = false;
			if (parentVariations.Any(childVariations.Contains)) return true;
			if (!TagTree.Keys.TryGetValue(key, out TagTree tagTree)) return false;
			if (parentVariations.Any(p => childVariations.Any(c => tagTree.IsDecendantOf(p, c)))) return true;

			isMoreSpecific = parentVariations.Any(p => childVariations.Any(c => tagTree.IsDecendantOf(c, p)));

			return false;
		}

		public static Tag GetMostDescriptiveTag(TagsCollectionBase tags)
		{
			var descriptiveKeys = new[] { "name", "amenity" };
			var key = descriptiveKeys.FirstOrDefault(k => tags.ContainsKey(k));
			return tags.FirstOrDefault(t => t.Key == key);
		}
	}
}
