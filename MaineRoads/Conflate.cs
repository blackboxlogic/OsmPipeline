using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Complete;
using OsmSharp.Streams;
using OsmSharp.Tags;

namespace MaineRoads
{
	public static class Conflate
	{
		// When matching, consider pulling capitalization and punctuation from local names.

		public static Osm FindMissingRoads(Osm subject, CompleteWay[] reference)
		{
			var missing = new List<CompleteWay>();
			var misShapen = new List<CompleteWay>();

			var keysToIndex = new Dictionary<string, Func<string, string[]>>() { { "name", Tags.WithOrWithoutPunctuation } };
			var subjectWays = subject.Ways
					.Where(e => e.Tags != null && e.Tags.ContainsKey("name") && e.Tags.ContainsKey("highway"))
					.ToArray();
			var subjectNodes = subject.Nodes.ToDictionary(n => n.Id.Value);
			var subjectElementIndex = new ElementIndex(subjectWays, keysToIndex);

			foreach (var referenceRoad in reference)
			{
				var searchTags = new TagsCollection(new Tag("name", referenceRoad.Tags["name"]));
				if (subjectElementIndex.TryGetMatchingElements(searchTags, out var candidates))
				{
					var candidateNodes = candidates.OfType<Way>().SelectMany(c => c.Nodes.Select(n => subjectNodes[n]));

					var byLat = new SortedList<double, Node[]>(candidateNodes.GroupBy(n => n.Latitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));
					var byLon = new SortedList<double, Node[]>(candidateNodes.GroupBy(n => n.Longitude.Value).ToDictionary(g => g.Key, g => g.ToArray()));

					var near = referenceRoad.Nodes.RatioWhere(n => BoundsExtentions.FastNodesInBounds(n.AsBounds().ExpandBy(30), byLat, byLon).Any());

					if (near == 0)
					{
						missing.Add(referenceRoad);
					}
					else if (near < 1)
					{
						// This needs to be defined better, maybe closeness to the line, vs closeness to a point.
						misShapen.Add(referenceRoad);
					}
				}
				else
				{
					missing.Add(referenceRoad);
				}
			}

			return missing.AsOsm();
		}
	}
}
