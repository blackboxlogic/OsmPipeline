using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Linq;
using System.Collections.Generic;
using OsmSharp.Tags;
using OsmSharp;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OsmPipeline
{
	public static class Conflate
	{
		private static ILogger Log;

		public static OsmChange Merge(
			ILoggerFactory loggerFactory, Osm reference, Osm subject)
		{
			Log = Log ?? loggerFactory.CreateLogger(typeof(Conflate));
			List<OsmGeo> create = new List<OsmGeo>();
			List<OsmGeo> modify = new List<OsmGeo>();

			var subjectNodesById = subject.Nodes.ToDictionary(n => n.Id.Value);
			var subjectWaysById = subject.Ways.ToDictionary(n => n.Id.Value);

			OsmChange change = new OsmChange();
			var subjectIndex = new OsmGeo[][] { subject.Nodes, subject.Ways, subject.Relations }
				.SelectMany(e => e)
				.GroupBy(e => GetAddrTags(e, false))
				.Where(g => g.Key.Any())
				.ToDictionary(g => g.Key, g => g.ToArray());

			foreach (var referenceElement in reference.Nodes)
			{
				var addr = referenceElement.GetAddrTags();
				if (subjectIndex.TryGetValue(addr, out var subjectElements))
				{
					if (subjectElements.Length > 1)
					{
						Log.LogWarning("Multiple matches!");
					}
					var subjectElement = subjectElements.First();
					var distance = Geometry.DistanceMeters(
						Geometry.GetCentroid(referenceElement, subjectNodesById, subjectWaysById),
						Geometry.GetCentroid(subjectElement, subjectNodesById, subjectWaysById));
					if (distance > 100)
					{
						Log.LogWarning("Address match is over 100 meters, ignoring the match");
						create.Add(referenceElement);
					}
					else if (MergeTags(referenceElement, subjectElement, distance))
					{
						subjectElement.Version++;
						modify.Add(subjectElement);
					}
					else
					{
						//Log.LogInformation("The tag merge didn't have any effect on the subject");
					}
				}
				else
				{
					create.Add(referenceElement);
				}
			}

			return new OsmChange() { Create = create.ToArray(), Modify = modify.ToArray() };
		}

		private static bool MergeTags(OsmGeo reference, OsmGeo subject, double distanceMeters)
		{
			var changed = false;

			if (subject is Node subjectNode && reference is Node referenceNode)
			{
				if (distanceMeters > 1)
				{
					subjectNode.Latitude = referenceNode.Latitude;
					subjectNode.Longitude = referenceNode.Longitude;
					changed = true;
				}
			}

			foreach (var tag in reference.Tags.Where(t => t.Key != "maineE911id"))
			{
				if (subject.Tags.TryGetValue(tag.Key, out string value))
				{
					if (value != tag.Value)
					{
						Log.LogError("A tag conflict!"); // middle school name conflict, alt name?
					}
				}
				else
				{
					changed = true;
					subject.Tags.Add(tag);
				}
			}

			return changed;
		}

		private static TagsCollection GetAddrTags(this OsmGeo element, bool all = false)
		{
			return all
				? new TagsCollection(element.Tags?.Where(t => t.Key.StartsWith("addr:")) ?? new Tag[0])
				: new TagsCollection(element.Tags?.Where(t => t.Key == "addr:street" || t.Key == "addr:housenumber") ?? new Tag[0]);
		}
	}
}
