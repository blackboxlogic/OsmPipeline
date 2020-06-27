using OsmSharp;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Tags
	{
		public static readonly Dictionary<string, string[]> AlternateKeys = new Dictionary<string, string[]>()
		{
			{ "addr:street", new [] { "addr:place" } },
			{ "building", new [] { "amenity" } },
			{ "name", new [] { "alt_name", "ref", "official_name", "old_name", "local_name", "short_name", "name:left", "name:right", "name_1" } }
		};

		public static readonly Dictionary<string, Func<string, string[]>> IndexableTagKeys =
			new Dictionary<string, Func<string, string[]>>()
			{
				{ "addr:street", Tags.WithOrWithoutPunctuation },
				{ "addr:place", Tags.WithOrWithoutPunctuation },
				{ "addr:housenumber", Tags.GetNumbersFromRangeOrList },
				{ "addr:unit", Tags.WithOrWithoutPunctuation },
				{ "name", Tags.WithOrWithoutPunctuation },
				{ "place", s => new []{ s } },
				{ "waterway", s => new []{ s } }
			};

		private static Func<string, string[]> GetMatchingFunction(string key)
		{
			return IndexableTagKeys.TryGetValue(key, out var value) ? value : GetPartsFromList;
		}

		public static string[] GetMatchingValues(Tag tag)
		{
			return GetMatchingFunction(tag.Key)(tag.Value);
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

		public static string[] UnitOmmisionable = new[] {
			"Apartment", "Building", "Department", "Floor", "Hanger", "House", "Key",
			"Lot", "Main", "Office", "Penthouse", "Pier", "Room", "Slip",
			"Space", "Stop", "Suite", "Trailer", "Unit", "#"};

		public static Dictionary<string, string[]> PrimaryElementKeys =
			new Dictionary<string, string[]> {
				{ "building", null },
				{ "aeroway", new [] { "aerodrome", "heliport", "helipad", "hanger" } },
				{ "man_made", new [] { "communications_tower", "tower", "lighthouse", "observatory", "pumping_station", "wastewater_plant", "water_tower", "works" } },
				{ "power", new [] { "generator", "substation", "plant" } },
				{ "tourism", null },
				{ "amenity", new [] { "ferry_terminal", "fire_station", "library", "restaurant", "post_office", "townhall", "place_of_worship", "school", "arts_centre" } },
				{ "shop", null }
			};

		public static bool IsMatchable(OsmGeo element)
		{
			return PrimaryElementKeys.Any(kvp => element.Tags != null
				&& element.Tags.TryGetValue(kvp.Key, out var value)
				&& (kvp.Value?.Contains(value) ?? true));
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

		// Return true if less specific
		// Return false if more specific
		public static bool TagMatchesTags(Tag tag, TagsCollectionBase tags, out bool isMoreSpecific, out string key)
		{
			isMoreSpecific = false;
			key = tag.Key;
			if (tags.ContainsKey(key) && ValuesMatch(key, tags[key], tag.Value, out isMoreSpecific)) return true;
			if (!AlternateKeys.TryGetValue(key, out string[] altKeys)) return false;

			foreach (var altKey in altKeys.Where(k => tags.ContainsKey(k)))
			{
				if (ValuesMatch(tag.Key, tags[altKey], tag.Value, out bool isAltKeyMoreSpecific))
				{
					key = altKey;
					return true;
				}

				isMoreSpecific |= isAltKeyMoreSpecific;
			}

			return false;
		}

		private static bool ValuesMatch(string key, string parent, string suspectedchild, out bool childIsMoreSpecific)
		{
			var matcher = GetMatchingFunction(key);
			var parentVariations = matcher(parent);
			var childVariations = matcher(suspectedchild);

			childIsMoreSpecific = false;
			if (parentVariations.Any(pv => childVariations.Contains(pv, StringComparer.OrdinalIgnoreCase))) return true;
			if (!TagTree.Keys.TryGetValue(key, out TagTree tagTree)) return false;
			if (parentVariations.Any(p => childVariations.Any(c => tagTree.IsDecendantOf(p, c)))) return true;

			childIsMoreSpecific = parentVariations.Any(p => childVariations.Any(c => tagTree.IsDecendantOf(c, p)));

			return false;
		}

		public static Tag GetMostDescriptiveTag(TagsCollectionBase tags)
		{
			var descriptiveKeys = new[] { "name", "amenity" };
			var key = descriptiveKeys.FirstOrDefault(k => tags.ContainsKey(k));
			return tags.FirstOrDefault(t => t.Key == key);
		}

		public static bool AreEqual(TagsCollectionBase left, TagsCollectionBase right)
		{
			return left.Count == right.Count && left.All(l => right.Contains(l));
		}

		public class TagsCollectionComparer : EqualityComparer<TagsCollectionBase>
		{
			public override bool Equals(TagsCollectionBase x, TagsCollectionBase y)
			{
				return AreEqual(x, y);
			}

			public override int GetHashCode(TagsCollectionBase obj)
			{
				return obj.Select(a => a.Key.GetHashCode() ^ a.Value.GetHashCode())
					.Aggregate(0, (a,b) => a^b);
			}
		}
	}
}
