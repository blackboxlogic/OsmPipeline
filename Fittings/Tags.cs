using OsmSharp;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OsmPipeline.Fittings
{
	public static class Tags
	{
		public static TagsCollection GetAddrTags(this OsmGeo element, bool all = false)
		{
			return all
				? new TagsCollection(element.Tags?.Where(t => t.Key.StartsWith("addr:")) ?? new Tag[0])
				: new TagsCollection(element.Tags?.Where(t => t.Key == "addr:street" || t.Key == "addr:housenumber") ?? new Tag[0]);
		}

		public static void AddOrAppend(this TagsCollectionBase tags, string key, string newValue)
		{
			if (tags.TryGetValue(key, out var oldValue))
			{
				newValue = oldValue + ";" + newValue;
			}

			tags.AddOrReplace(key, newValue);
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
	}
}
