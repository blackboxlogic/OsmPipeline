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

		public ElementIndex(ICollection<OsmGeo> elements)
		{
			TagKeyTagValueElements = Tags.MatchableTagKeys.ToDictionary(
				keyOptionsPair => keyOptionsPair.Key,
				keyOptionsPair => elements
					.Where(e => e.Tags != null && e.Tags.ContainsKey(keyOptionsPair.Key))
					.SelectMany(element => keyOptionsPair.Value(element.Tags[keyOptionsPair.Key])
						.Select(tagValue => new { tagValue, element }))
					.GroupBy(ne => ne.tagValue)
					.ToDictionary(g => g.Key, g => g.Select(ne => ne.element).ToArray()));
		}

		public bool TryGetMatchingElements(TagsCollectionBase keyTags, out OsmGeo[] values)
		{
			values = null;

			var matchableTags = keyTags.Where(tag => Tags.MatchableTagKeys.ContainsKey(tag.Key)).ToArray();
			if (matchableTags.Length < 2) throw new Exception("Tried to match on 1 tag.");

			foreach (var tag in matchableTags)
			{
				if (!TryGetValue(tag, out OsmGeo[] elements)) return false;
				values = values?.Intersect(elements).ToArray() ?? elements;
				if (!values.Any()) return false;
			}

			return true;
		}

		private bool TryGetValue(Tag tag, out OsmGeo[] elements)
		{
			elements = null;

			if (TagKeyTagValueElements.TryGetValue(tag.Key, out var tagValues)
					&& tagValues.TryGetValue(tag.Value, out elements))
				return true;

			if (!Tags.AlternateKeys.TryGetValue(tag.Key, out string[] altKeys)) return false;

			foreach (string altKey in altKeys)
			{
				if (TryGetValue(new Tag(altKey, tag.Value), out elements)) return true;
			}

			return false;
		}
	}
}
