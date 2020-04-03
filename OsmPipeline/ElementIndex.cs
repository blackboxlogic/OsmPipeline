using System;
using System.Linq;
using System.Collections.Generic;
using OsmSharp;
using OsmPipeline.Fittings;
using OsmSharp.Tags;

namespace OsmPipeline
{
	public class ElementIndex
	{
		private readonly Dictionary<string, Dictionary<string, OsmGeo[]>> TagKeyTagValueElements;
		public readonly Dictionary<string, OsmGeo> ByOsmGeoKey;

		public ICollection<OsmGeo> Elements => ByOsmGeoKey.Values;

		public ElementIndex(ICollection<OsmGeo> elements)
		{
			ByOsmGeoKey = elements.ToDictionary(n => n.Type.ToString() + n.Id); // should use OsmGeoKey
			TagKeyTagValueElements = Tags.IndexableTagKeys.ToDictionary(
				keyOptionsPair => keyOptionsPair.Key,
				keyOptionsPair => elements
					.Where(e => e.Tags != null && e.Tags.ContainsKey(keyOptionsPair.Key))
					.SelectMany(element => keyOptionsPair.Value(element.Tags[keyOptionsPair.Key])
						.Select(tagValue => new { tagValue, element }))
					.GroupBy(ne => ne.tagValue, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.Select(ne => ne.element).ToArray(), StringComparer.OrdinalIgnoreCase));
			foreach (var valueDictionary in TagKeyTagValueElements.Values)
			{
				valueDictionary.Add("*", valueDictionary.Values.SelectMany(v => v).Distinct().ToArray());
			}
		}

		// Search tags are expected to be in connonical form, missing punctuation, single house numbers
		public bool TryGetMatchingElements(TagsCollectionBase searchTags, out OsmGeo[] values)
		{
			values = null;

			foreach (var tag in searchTags)
			{
				if (!TryGetElements(tag, out OsmGeo[] elements)) return false;
				values = values?.Intersect(elements).ToArray() ?? elements;
				if (!values.Any()) return false;
			}

			return true;
		}

		private bool TryGetElements(Tag tag, out OsmGeo[] elements)
		{
			elements = null;

			if (TagKeyTagValueElements.TryGetValue(tag.Key, out var tagValues)
				&& tagValues.TryGetValue(tag.Value, out elements)) return true;

			if (!Tags.AlternateKeys.TryGetValue(tag.Key, out string[] altKeys)) return false;

			foreach (string altKey in altKeys)
			{
				if (TryGetElements(new Tag(altKey, tag.Value), out elements)) return true;
			}

			return false;
		}
	}
}
