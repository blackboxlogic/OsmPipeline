using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsmPipeline.Fittings;
using OsmSharp.Changesets;
using OsmSharp;
using Microsoft.Extensions.Logging;
using OsmSharp.IO.API;
using OsmSharp.Tags;

namespace OsmPipeline
{
	public static class OneOffs
	{
		public static void RecreateMissingDiffResultFiles()
		{
			Static.Municipalities = FileSerializer.ReadJson<Dictionary<string, GeoJsonAPISource.Municipality>>("MaineMunicipalities.json");
			var osmApiClient = new NonAuthClient(Static.Config["OsmApiUrl"], Static.HttpClient,
				Static.LogFactory.CreateLogger<NonAuthClient>());

			foreach (var municipality in Static.Municipalities.Values.Where(m => m.ChangeSetIds.Any(id => id != -1)))
			{
				foreach (var changeId in municipality.ChangeSetIds)
				{
					var diffPath = $"{municipality.Name}/Uploaded/{changeId}-DiffResult.diff";

					if (!File.Exists(diffPath))
					{
						Console.WriteLine(diffPath);

						var changePath = $"{municipality.Name}/Uploaded/{changeId}-Conflated.osc";
						var change = FileSerializer.ReadXml<OsmChange>(changePath);
						var mine = change.Create.OfType<Node>().ToArray();
						var theirs = osmApiClient.GetChangesetDownload(changeId).Result;
						List<OsmGeoResult> results = theirs.Modify.Select(e => e.AsDiffResult()).ToList();
						var map = Geometry.NodesInOrNearCompleteElements(theirs.Create.OfType<Node>().ToArray(), mine, 0, 0, new HashSet<long>());
						if (map.Count != theirs.Create.Length || map.Any(pair => pair.Value.Count(v => Tags.AreEqual(v.Tags, pair.Key.Tags)) != 1))
							throw new Exception("bad map");
						results.AddRange(map.Select(pair => pair.Value.Single(v => Tags.AreEqual(v.Tags, pair.Key.Tags)).AsDiffResult(pair.Key.Id, 1)));
						var diffResult = new DiffResult() { Version = 0.6, Generator = "OsmPipeline", Results = results.ToArray() };
						FileSerializer.WriteXml(diffPath, diffResult);
					}
				}
			}
		}

		public static IEnumerable<OsmGeo> GetAllChangeElements(Func<OsmGeo, bool> filter)
		{
			foreach(var change in GetAllChangeFiles())
			{
				var changedElements = change.Create.Concat(change.Modify);

				foreach (var changedElement in changedElements)
				{
					yield return changedElement;
				}
			}
		}

		private static IEnumerable<OsmChange> GetAllChangeFiles()
		{
			Static.Municipalities = FileSerializer.ReadJson<Dictionary<string, GeoJsonAPISource.Municipality>>("MaineMunicipalities.json");
			var osmApiClient = new NonAuthClient(Static.Config["OsmApiUrl"], Static.HttpClient,
				Static.LogFactory.CreateLogger<NonAuthClient>());

			foreach (var municipality in Static.Municipalities.Values.Where(m => m.ChangeSetIds.Any(id => id != -1)))
			{
				foreach (var changeId in municipality.ChangeSetIds)
				{
					var changePath = $"{municipality.Name}/Uploaded/{changeId}-Conflated.osc";
					var change = FileSerializer.ReadXml<OsmChange>(changePath);
					yield return change;
				}
			}
		}
	}
}
