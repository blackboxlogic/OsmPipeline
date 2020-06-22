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
using System.Threading;
using OsmSharp.API;
using System.Text;
using OsmSharp.Db.Impl;
using OsmSharp.Db;
using OsmSharp.Streams;

namespace OsmPipeline
{
	public static class OneOffs
	{
		public static readonly IAuthClient OsmApiClient = new OsmSharp.IO.API.BasicAuthClient(Static.HttpClient,
				Static.LogFactory.CreateLogger<OsmSharp.IO.API.BasicAuthClient>(), Static.Config["OsmApiUrl"],
				Static.Config["OsmUsername"], Static.Config["OsmPassword"]);

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

		public static void GetRoutes()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\maine-latest-internal.osm.pbf").ToArray();
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm));
			var routes = osm.Where(e => e.Tags != null && e.Tags.ContainsKey("ref"))
				.Select(e => e.Tags["ref"])
				.SelectMany(re => re.Split(";"))
				.Distinct()
				.OrderBy(e => e)
				.ToArray();

			foreach (var route in routes.Where(re => re.StartsWith("US")))
			{
				Console.WriteLine(route);
			}

			foreach (var route in routes.Where(re => re.StartsWith("ME")))
			{
				Console.WriteLine(route);
			}
		}

		public static void CountNoName()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\Maine\maine-latest-internal.osm.pbf").ToArray();

			var nodes = osm.OfType<Node>().ToDictionary(n => n.Id, n => n.AsPosition());

			var junctions = new HashSet<long>(osm.OfType<Way>().SelectMany(w => w.Nodes).GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key));

			var noName = osm.Where(e => e is Way && e.Tags != null && e.Tags.ContainsKey("highway") && !e.Tags.ContainsKey("name") && !e.Tags.ContainsKey("ref"))
				.GroupBy(e => e.Tags["highway"])
				.ToDictionary();

			var residential = noName["residential"].OfType<Way>().Where(r => r.Nodes.Count(n => junctions.Contains(n)) == 1
				&& Geometry.DistanceMeters(nodes[r.Nodes.First()], nodes[r.Nodes.Last()]) < 100).ToArray();

			var residentialNodes = new HashSet<long>(residential.SelectMany(r => r.Nodes));

			var elements = residential.OfType<OsmGeo>().Concat(osm.OfType<Node>().Where(n => residentialNodes.Contains(n.Id.Value)));

			FileSerializer.WriteXml("FIXCountNoName\\FIXCountNoName.osm", elements.AsOsm());
			
			Console.WriteLine(residential.Length);

			//Console.WriteLine(SummarizeGrouping(noName));
		}

		public static void GetSegments()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\Franklin_Full_Street_Name_simplified.osm").ToArray();
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm));
			var highways = osm.OfType<Way>()
				.Where(w => w.Tags != null && w.Tags.ContainsKey("highway") && w.Tags.ContainsKey("name")).ToList();
			var touchers = highways.GroupBy(w => w.Tags["name"])
				.SelectMany(g => g.Where(w => g.Any(other => w != other && EndsAreTouching(w, other))))
				.WithChildren(index)
				.AsOsm();
			FileSerializer.WriteXml("Touchers.osm", touchers);
		}

		public static void CombineSegments()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\Franklin_Full_Street_Name_simplified.osm").ToArray();
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm));
			var highways = osm.OfType<Way>()
				.Where(w => w.Tags != null && w.Tags.ContainsKey("highway") && w.Tags.ContainsKey("name")).ToHashSet();
			var highwaysGrouped = highways.GroupBy(h => h.Tags["name"]);
			var doneNames = new HashSet<string>();

			restart: // gross but effective
			var highwaysByNames = highwaysGrouped.Where(g => g.Count() > 1 && !doneNames.Contains(g.Key));

			foreach (var byName in highwaysByNames)
			{
				var ends = byName.SelectMany(highway => new[] { highway.Nodes.First(), highway.Nodes.Last() }.Select(node => new { node, highway }))
					.GroupBy(nh => nh.node).Select(g => g.Select(gw => gw.highway).Distinct());

				var intersection = ends.FirstOrDefault(g => g.Count() > 1)?.ToArray();
				if (intersection != null)
				{
					CombineSegments(intersection[0], intersection[1]);
					highways.Remove(intersection[1]);
					goto restart;
				}
				else
				{
					doneNames.Add(byName.Key);
				}
			}

			FileSerializer.WriteXml("Touchers.osm", highways.WithChildren(index).AsOsm());
		}

		private static void CombineSegments(Way subject, Way reference)
		{
			if (!subject.Tags.Equals(reference.Tags)) throw new Exception("these tags are different, can't merge ways");

			if (subject.Nodes.Last() == reference.Nodes.First())
			{
				subject.Nodes = subject.Nodes.Concat(reference.Nodes.Skip(1)).ToArray();
			}
			else if (subject.Nodes.Last() == reference.Nodes.Last())
			{
				if (reference.Tags != null && reference.Tags.ContainsKey("oneway")) throw new Exception("Reversing a oneway");
				subject.Nodes = subject.Nodes.Concat(reference.Nodes.Reverse().Skip(1)).ToArray();
			}
			else if (subject.Nodes.First() == reference.Nodes.Last())
			{
				subject.Nodes = reference.Nodes.Concat(subject.Nodes.Skip(1)).ToArray();
			}
			else if (subject.Nodes.First() == reference.Nodes.First())
			{
				if (reference.Tags != null && reference.Tags.ContainsKey("oneway")) throw new Exception("Reversing a oneway");
				subject.Nodes = reference.Nodes.Reverse().Concat(subject.Nodes.Skip(1)).ToArray();
			}
			else
			{
				throw new Exception("Ways are not end-to-end, and can't be combined");
			}
		}

		private static bool EndsAreTouching(Way a, Way b)
		{
			var aEnds = new [] { a.Nodes.First(), a.Nodes.Last() };
			var bEnds = new [] { b.Nodes.First(), b.Nodes.Last() };
			return aEnds.Intersect(bEnds).Any();
		}

		public static void SummarizeKeys()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\maine-latest-internal.osm.pbf")
				.Where(e => e.UserId == 10307443).ToArray();
			FileSerializer.WriteXml("blackboxlogic.osm", osm.AsOsm());
		}

		public static void FIXDupeNames()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\maine-latest-internal.osm.pbf");
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm));

			var named = osm.Where(e => (e is Node || e is Way) && e.Tags != null && e.Tags.ContainsKey("name") && !e.Tags.ContainsKey("highway") && !e.Tags.ContainsKey("railway") && !e.Tags.ContainsKey("waterway"))
				.GroupBy(e => Connonical(e.Tags["name"]))
				.Where(g => g.Count() > 1)
				.ToDictionary();
			
			var dupes = named.ToDictionary(kvp => kvp.Key, kvp => Geometry.GroupCloseNeighbors(kvp.Value, 1000, index, false).Where(c=> c.Count > 1).OrderByDescending(c => c.Count).ToArray()).OrderByDescending(d => d.Value.Length).ToArray();
			var dupeCount = dupes.SelectMany(d => d.Value.SelectMany(r => r)).Count();
			var av = dupes.SelectMany(d => d.Value).Average(d => d.Count);
			var ma = dupes.SelectMany(d => d.Value).Max(d => d.Count);
			var myDupes = dupes.ToDictionary(d => d.Key, d => d.Value.Where(c => c.Any(e => e.UserName.StartsWith("blackbo"))).ToArray()).Where(c => c.Value.Any()).OrderByDescending(c => c.Value.Count()).ToArray();
			var myDupeCount = myDupes.SelectMany(d => d.Value.SelectMany(r => r)).Count();

			var myOsm = myDupes.SelectMany(d => d.Value.SelectMany(r => r)).ToArray().WithChildren(index).AsOsm();
			FileSerializer.WriteXml("FIXDupeNames\\myDupes.osm", myOsm);

			var allOsm = dupes.SelectMany(d => d.Value.SelectMany(r => r)).ToArray().WithChildren(index).AsOsm();
			FileSerializer.WriteXml("FIXDupeNames\\allDupes.osm", allOsm);
		}

		public static void FixPlaces()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\maine-latest-internal.osm.pbf").ToArray();

			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm));

			var roadsByName = osm.Where(e => e.Tags != null && e.Tags.ContainsKey("highway") && e.Tags.ContainsKey("name"))
				.GroupBy(e => Connonical(e.Tags["name"])).ToDictionary();
			var placesByName = osm.Where(e => e.Tags != null && e.Tags.ContainsKey("name") && (e.Tags.ContainsKey("place") || e.Tags.ContainsKey("waterway")))
				.GroupBy(e => Connonical(e.Tags["name"])).ToDictionary();
			var orphans = osm.Where(e => e.Tags != null && e.Tags.Contains("addr:state", "ME") && e.Tags.ContainsKey("addr:street") && !roadsByName.ContainsKey(Connonical(e.Tags["addr:street"]))
				&& placesByName.ContainsKey(Connonical(e.Tags["addr:street"]))
				&& placesByName[Connonical(e.Tags["addr:street"])].Any(
					p => Geometry.MinDistanceMeters(e.AsComplete(index).AsPosition(), p.AsComplete(index)) < 1000)).ToArray();

			var sad = osm.Where(e => e.Tags != null && e.Tags.ContainsKey("addr:street") && e.Tags["addr:street"].Contains("Island") && !roadsByName.ContainsKey(Connonical(e.Tags["addr:street"]))
				&& (!placesByName.ContainsKey(Connonical(e.Tags["addr:street"]))
				|| !placesByName[Connonical(e.Tags["addr:street"])].Any(
					p => Geometry.MinDistanceMeters(e.AsComplete(index).AsPosition(), p.AsComplete(index)) < 1000))).ToArray();

			FileSerializer.WriteXml(@"Fix IslandPlaces\islands.osm", placesByName.Values.SelectMany(ps => ps).WithChildren(index).AsOsm());
			FileSerializer.WriteXml(@"Fix IslandPlaces\orphans.osm", orphans.WithChildren(index).AsOsm());
			FileSerializer.WriteXml(@"Fix IslandPlaces\sadOrphans.osm", sad.WithChildren(index).AsOsm());

			foreach (var orphan in orphans)
			{
				orphan.Tags["addr:place"] = orphan.Tags["addr:street"];
				orphan.Tags.RemoveKey("addr:street");
			}

			var change = Changes.FromGeos(null, orphans, null);
			var changeTags = GetCommitTags("Moving addr:street tag to addr:place for island addresses.");
			var ids = Subjects.UploadChange(change, changeTags, "Fix IslandPlaces").Result;
		}

		public static void FixIslandPlaces()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\Maine\maine-latest-internal.osm.pbf").ToArray();
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm));

			var roadNames = new HashSet<string>(osm.Where(e => e.Tags != null && e.Tags.ContainsKey("highway") && e.Tags.ContainsKey("name")).Select(e => Connonical(e.Tags["name"])).Distinct());
			var places = osm.Where(e => e.Tags != null && (e.Tags.ContainsKey("place") || e.Tags.ContainsKey("waterway")));
			var islands = places.Where(e => e.Tags["place"] == "island" || e.Tags["place"] == "islet" || e.Tags.Any(t => t.Key == "name" && t.Value.Contains("island", StringComparison.OrdinalIgnoreCase))).ToArray();
			var islandsByName = islands.Where(e => e.Tags.ContainsKey("name")).GroupBy(e => Connonical(e.Tags["name"])).ToDictionary();

			var orphans = osm.Where(e => e.Tags != null && e.Tags.ContainsKey("addr:street") && !roadNames.Contains(Connonical(e.Tags["addr:street"]))
				&& islandsByName.ContainsKey(Connonical(e.Tags["addr:street"]))).ToArray();

			var matchedOrphans = orphans.Where(e => islandsByName[Connonical(e.Tags["addr:street"])].Any(i => Geometry.MinDistanceMeters(e.AsComplete(index).AsPosition(), i.AsComplete(index)) < 1000)).ToArray();

			FileSerializer.WriteXml(@"Fix IslandPlaces\islands.osm", islands.WithChildren(index).AsOsm());
			FileSerializer.WriteXml(@"Fix IslandPlaces\orphans.osm", orphans.WithChildren(index).AsOsm());
			FileSerializer.WriteXml(@"Fix IslandPlaces\matchedOrphans.osm", matchedOrphans.WithChildren(index).AsOsm());
			FileSerializer.WriteXml(@"Fix IslandPlaces\unmatchedOrphans.osm", orphans.Except(matchedOrphans).WithChildren(index).AsOsm());

			foreach (var orphan in orphans)
			{
				orphan.Tags["addr:place"] = orphan.Tags["addr:street"];
				orphan.Tags.RemoveKey("addr:street");
			}

			var change = Changes.FromGeos(null, orphans, null);
			var changeTags = GetCommitTags("Moving addr:street tag to addr:place for island addresses.");
			var ids = Subjects.UploadChange(change, changeTags, "Fix IslandPlaces").Result;
		}

		public static void UnlinkGNIS()
		{
			var osm = OpenFile(@"C:\Users\Alex\Downloads\Maine\maine-latest-internal.osm.pbf").ToArray();

			var badsC = osm.Where(e => e.Tags != null && e.Tags.Contains("gnis:reviewed", "no") && e.UserName == "blackboxlogic+bot").ToDictionary(b => b.Id);

			var bads = osm.Where(e => e.Tags != null && e.Tags.Contains("gnis:reviewed", "no") && e.UserName == "blackboxlogic+bot" && e is Node).ToDictionary(b => b.Id);
			var prevs = OsmApiClient.GetElements(bads.Values.ToDictionary(b => new OsmGeoKey(b), b => b.Version - 1)).Result;
			var news = new List<OsmGeo>();
			var changes = new List<OsmGeo>();
			foreach (var prev in prevs.Where(p => !p.Tags.ContainsKey("addr:street")
				&& p.Id != 367794548 && p.Id != 367795033 && p.Id != 367794682 && p.Id != 367794721 && p.Id != 367795011
				&& p.Id != 367795009 && p.Id != 367794614 && p.Id != 367794612))
			{
				prev.Version++;
				changes.Add(prev);

				var bad = bads[prev.Id];
				bad.Tags = new TagsCollection(bad.Tags.Where(t => t.Key.StartsWith("addr")));
				bad.Id = -bad.Id;
				news.Add(bad);
			}

			var change = Changes.FromGeos(news, changes, null);
			FileSerializer.WriteXml("FIX unmerge gnis\\change.osc", change);
			var changeTags = GetCommitTags("Separate addreses which should not have been merged into GNIS elements with reviewed=no");
			var ids = Subjects.UploadChange(change, changeTags, "FIX unmerge gnis").Result;

			//Console.WriteLine(string.Join(Environment.NewLine, bads.Values.Select(b => b.Type + " " + b.Id)));
		}

		public static void FixAddrStreetFormat()
		{
			// or @"C:\Users\Alex\Downloads\Maine\maine-latest.osm\maine-latest.osm"
			var osm = OpenFile(@"C:\Users\Alex\Downloads\Maine\maine-latest-internal.osm.pbf");
			var index = osm.ToDictionary(e => new OsmGeoKey(e));
			var stringdex = index.Values.ToDictionary(e => e.Type.ToString() + e.Id);
			var canonNamesToCompleteWays = index.Values
				.OfType<Way>()
				.Where(e => e.Tags != null && e.Tags.ContainsKey("highway") && e.Tags.ContainsKey("name"))
				.Select(w => w.AsComplete(stringdex))
				.GroupBy(e => Connonical(e.Tags["name"]))
				.ToDictionary();

			var addrStreets = index.Values.Where(e => e.Tags != null && e.Tags.Contains("addr:state", "ME") && e.Tags.ContainsKey("addr:street"))
				.Select(element => new { element, addrStreet = element.Tags["addr:street"], canon = Connonical(element.Tags["addr:street"]), position = element.AsComplete(stringdex).AsPosition() } )
				.Where(e => canonNamesToCompleteWays.ContainsKey(e.canon))
				.ToArray();

			var changes = new List<OsmGeo>();

			foreach (var addr in addrStreets)
			{
				var roadMatches = canonNamesToCompleteWays[addr.canon]
					.Where(r => Geometry.MinDistanceMeters(addr.position, r) < 1000).ToArray();
				var roadNameMatches = roadMatches.Select(r => r.Tags["name"]).Distinct().ToArray();
				if (roadNameMatches.Length == 0)
				{

				}
				else if (roadNameMatches.Length > 1)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("conflict " + addr.element.Type + " " + addr.element.Id + " -> " + string.Join(", ", roadNameMatches));
					Console.ForegroundColor = ConsoleColor.White;

					roadMatches.First().Tags.Add("fixme",
						"Different road segments have name formats conflict. Check the street sign and fix: " +
						string.Join(" vs ", roadNameMatches.Select(n => '[' + n + ']')));
					//changes.Add(roadMatches.First().AsSimple());
				}
				else if (roadNameMatches[0] != addr.addrStreet)
				{
					//if (WhitespacesDiffers(addr.addrStreet, roadNameMatches[0]) && addr.element.UserName.StartsWith("blackboxlogic"))
					//{
					//	var did = false;
					//	foreach (var road in roadMatches)
					//	{
					//		if (!road.Tags.Contains("official_name", addr.addrStreet))
					//		{
					//			did = true;
					//			road.Tags.AddOrReplace("official_name", addr.addrStreet);
					//			changes.Add(road.AsOsmGeo());
					//		}
					//	}

					//	if (did) Console.WriteLine("O\t" + roadMatches.Length + "x " + roadNameMatches.First() + " -> " + addr.addrStreet);
					//}

					Console.WriteLine(addr.addrStreet + " -> " + roadNameMatches.First());
					addr.element.Tags["addr:street"] = roadNameMatches.First();
					changes.Add(addr.element);

					
				}
			}

			var change = Changes.FromGeos(null, changes, null);
			var ids = Subjects.UploadChange(change, GetCommitTags("Adjusting addr:street format to match nearby street name (case, punctuation, space)"), "Fix addrStreet Format" ).Result;
			//var streetNameConflicts = connonicalStreetNames.Where(c => c.Value.Length > 1)
			//	.ToDictionary(c => c.Key, c => FightingWays(c.Value.SelectMany(n => streetNamesToWays[n].Ways)));
			//Console.WriteLine(string.Join(Environment.NewLine,
			//	streetNameConflicts.Where(kvp => kvp.Value.Any()).Select(kvp => kvp.Key + ": " + "\n\t" +
			//		string.Join("\n\t", kvp.Value.Select(w => "osm.org/way/" + w.Id + "\t" + wayIndex[w.Id].Tags["name"])))));
		}

		// Parts of the same road with different highway tags?
		// E911 ways with no nearby canon name matchin OSM

		private static bool WhitespacesDiffers(string a, string b)
		{
			return a.Count(c => c == ' ') != b.Count(c => c == ' ');
		}

		private static Dictionary<string, OsmGeo[]> GroupElementsByTag(IEnumerable<OsmGeo> elements, string key)
		{
			var groupings = elements
				.Where(e => e.Tags != null && e.Tags.ContainsKey(key))
				.GroupBy(e => e.Tags[key])
				.ToDictionary();
			return groupings;
		}

		private static string SummarizeGrouping(Dictionary<string, OsmGeo[]> groups)
		{
			return string.Join(Environment.NewLine,
				groups.OrderByDescending(kvp => kvp.Value.Length).Select(kvp => kvp.Key + " x" + kvp.Value.Length));
		}

		private static string Connonical(string name)
		{
			var builder = new StringBuilder(name);
			var specials = new Dictionary<char, char>() { { 'ô', 'o' }, { 'à', 'a' }, { 'ç', 'c' }, { 'é', 'e' }, { 'è', 'e' }, { 'É', 'E' } };

			foreach (var special in specials)
				builder.Replace(special.Key, special.Value);

			foreach (var c in builder.ToString().Where(c => !char.IsLetterOrDigit(c)))
				builder.Replace(c.ToString(), "");

			return builder.ToString().ToUpper();
		}

		public static void FixImportedNames()
		{
			var nameIndex = OneOffs.GetAllChangeElements(e => e.Tags != null && e.Tags.ContainsKey("addr:unit"))
				.GroupBy(e => e.Tags["addr:unit"])
				.ToDictionary(g => g.Key, g => g.ToArray());

			Console.Write(string.Join(Environment.NewLine,
				nameIndex.OrderByDescending(kvp => kvp.Value.Length).Select(kvp => kvp.Value.Length + "x: " + kvp.Key + "\texample: " + kvp.Value.First().Type + " " + kvp.Value.First().Id)));
			var nameDestinations = new Dictionary<string, string>
				{ };
			var toUpdate = new List<OsmGeo>();

			var currentKeys = nameDestinations.Keys.SelectMany(n => nameIndex[n].Select(e => new OsmGeoKey(e))).Distinct().ToArray();
			var allCurrents = GetCurrentVersionOfElements(currentKeys).ToDictionary(e => new OsmGeoKey(e));
			var missing = currentKeys.Except(allCurrents.Keys).Select(k => k.Type + " " + k.Id).ToArray();

			foreach (var name in nameDestinations)
			{
				var keys = nameIndex[name.Key].Select(e => new OsmGeoKey(e)).ToArray();

				var currents = keys
					.Where(k => allCurrents.ContainsKey(k))
					.Select(k => allCurrents[k])
					.Where(e => e.Visible == true && e.Tags != null && e.Tags.Contains("addr:unit", name.Key))
					.ToArray();

				foreach (var element in currents)
				{
					if(name.Value == null)
						element.Tags.RemoveKey("addr:unit");
					else
						element.Tags.AddOrReplace("addr:unit", name.Value);

					toUpdate.Add(element);
				}
			}

			var fix = new OsmChange() { Modify = toUpdate.ToArray() };
			FileSerializer.WriteXml("temp change.osc", fix);
			var changesetTags = GetCommitTags($"Fixing bad addr:unit previously imported.");
			var fixIds = Subjects.UploadChange(fix, changesetTags, @"FIX Offices\Uploaded\").Result;
		}

		public static IEnumerable<OsmGeo> GetAllChangeElements(Func<OsmGeo, bool> filter = null)
		{
			filter = filter ?? (e => true);

			foreach (var changeDiff in GetAllChangeFiles())
			{
				var changedElements = changeDiff.Item1.GetElements().Where(filter);
				var diffiontary = changeDiff.Item2.Results.ToDictionary(r => r.OldId, r => r.NewId);

				foreach (var changedElement in changedElements)
				{
					changedElement.Id = diffiontary[changedElement.Id];
					yield return changedElement;
				}
			}
		}

		public static IEnumerable<Tuple<OsmChange, DiffResult>> GetAllChangeFiles()
		{
			Static.Municipalities = FileSerializer.ReadJson<Dictionary<string, GeoJsonAPISource.Municipality>>("MaineMunicipalities.json");

			foreach (var municipality in Static.Municipalities.Values)
			{
				foreach (var changeId in municipality.ChangeSetIds)
				{
					var changePath = $"{municipality.Name}/Uploaded/{changeId}-Conflated.osc";
					var change = FileSerializer.ReadXml<OsmChange>(changePath);
					var diffPath = $"{municipality.Name}/Uploaded/{changeId}-DiffResult.diff";
					var diff = FileSerializer.ReadXml<DiffResult>(diffPath);

					yield return Tuple.Create(change, diff);
				}
			}
		}

		public static long[] UndoTagFromAllUserChanges(long userId, Tag badTag)
		{
			var changesetids = OneOffs.GetUsersChangesetIds(userId);
			var changes = OneOffs.GetElementsFromChanges(changesetids);
			var badChanges = changes.Where(e => e.Tags != null && e.Tags.Contains(badTag)).Select(e => new OsmGeoKey(e)).Distinct().ToArray();
			var currentVersions = OsmApiClient.GetElements(badChanges).Result;
			var fix = OneOffs.UndoTagFromElements(badTag, currentVersions);
			var changesetTags = GetCommitTags($"Undo all instances of {badTag} from user {userId}.");
			var fixIds = Subjects.UploadChange(fix, changesetTags, @"FIX Offices\Uploaded\").Result;
			return fixIds;
		}

		public static long[] UndoGolfPaths(long userId)
		{

			//var modified = Translate.DistinctBy(GetChangesFromChangesets(changeIds).SelectMany(c => c.Delete).Where(e => !(e is Node)), e => new OsmGeoKey(e))
			//	.ToDictionary(e => new OsmGeoKey(e), e => e.Version - 1);

			//var withDetails = FileSerializer.ReadXmlCacheOrSource("FIX GolfPaths\\deletes.osm",
			//	() => OsmApiClient.GetElements(modified).Result.AsOsm());

			//var summary = withDetails.GetElements().Where(e => e.Tags != null).SelectMany(e => e.Tags).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
			//foreach (var sum in summary.OrderByDescending(g => g.Value))
			//{
			//	Console.WriteLine("x" + sum.Value + "\t" + sum.Key);
			//}

			//var golfPaths = withDetails.GetElements().Where(e => e.Tags != null && e.Tags.ContainsKey("highway"))
			//	.OrderBy(e => e.Version)
			//	.GroupBy(e => new OsmGeoKey(e))
			//	.ToDictionary(g => g.Key, g => g.First());



			// Find all ways modified with cart paths
			var changeIds = GetUsersChangesetIds(userId);
			var modified = GetChangesFromChangesets(changeIds).SelectMany(c => c.Modify).ToArray();
			var golfPaths = modified.Where(e => e is Way way && way.Tags != null && way.Tags.Contains("golf", "path"))
				.OrderBy(e => e.Version)
				.GroupBy(e => new OsmGeoKey(e))
				.ToDictionary(g => g.Key, g => g.First());
			var previousVersion = OsmApiClient.GetElements(golfPaths.ToDictionary(e => e.Key, e => e.Value.Version - 1)).Result.ToDictionary(e => new OsmGeoKey(e));
			var currentVersion = OsmApiClient.GetElements(golfPaths.Keys.ToArray()).Result.ToDictionary(e => new OsmGeoKey(e));

			foreach (var golfPath in golfPaths)
			{
				Console.Write(golfPath.Key.Type + " " + golfPath.Key.Id);
				var previous = previousVersion[golfPath.Key];
				var current = currentVersion[golfPath.Key];

				if (previous.Tags != null && previous.Tags.TryGetValue("highway", out string prevHighway) // had a highway tag "prevHighway"
					&& (current.Visible == false || current.Tags == null || !current.Tags.Contains("highway", prevHighway)))
				{
					Console.Write(" bad: " + current.Visible + "  " + (current.Tags == null) + "  " + !current.Tags?.Contains("highway", prevHighway) + " was " + prevHighway);
				}

				Console.WriteLine();
			}

			

			// Find all previous versions of those elements, that were roads
			// find all current versions of those that are not roads
			// change them back to what they were before

			return new[] { 0L };
		}

		public static long[] UndoTagFromChange(Tag badTag, long changeId)
		{
			var osmGeoKeys = GetElementsFromChanges(changeId)
				.Where(e => e.Tags != null && e.Tags.Contains(badTag))
				.Select(e => new OsmGeoKey(e)).ToArray();
			var currentElements = GetCurrentVersionOfElements(osmGeoKeys);
			var fix = UndoTagFromElements(badTag, currentElements);
			var changesetTags = GetCommitTags($"Undo all instances of {badTag} from commit {changeId}.");
			var fixIds = Subjects.UploadChange(fix, changesetTags, @"FIX Offices\Uploaded\").Result;
			return fixIds;
		}

		public static OsmGeo[] GetCurrentVersionOfElements(OsmGeo[] elements)
		{
			return GetCurrentVersionOfElements(elements.Select(e => new OsmGeoKey(e)).ToArray());
		}

		public static OsmGeo[] GetCurrentVersionOfElements(OsmGeoKey[] keys)
		{
			var updatedElements = OsmApiClient.GetElements(keys).Result;
			return updatedElements.Where(e => e.Visible == true).ToArray();
		}

		public static OsmChange UndoTagFromElements(Tag tagToRemove, OsmGeo[] osmGeos)
		{
			var stillBad = osmGeos.Where(e => e.Tags != null && e.Tags.Contains(tagToRemove) && e.Visible == true).ToArray();

			foreach (var e in stillBad)
			{
				e.Tags.RemoveKeyValue(tagToRemove);
			}

			var theFix = Changes.FromGeos(null, stillBad, null);
			return theFix;
		}

		public static OsmChange[] GetChangesFromChangesets(params long[] changeIds)
		{
			var changes = new List<OsmChange>();

			foreach (var changeId in changeIds)
			{
				OsmChange change = FileSerializer.ReadXmlCacheOrSource(@"CachesChanges\" + changeId + ".osc",
					() => { Thread.Sleep(15000); return OsmApiClient.GetChangesetDownload(changeId).Result; });
				changes.Add(change);
			}

			return changes.ToArray();
		}

		public static OsmGeo[] GetElementsFromChanges(params long[] changeIds)
		{
			var elements = new List<OsmGeo>();
			int i = 0;
			foreach (var changeId in changeIds)
			{
				Console.WriteLine(++i +"/"+ changeIds.Length + ": " + changeId);
				OsmChange fullChange = FileSerializer.ReadXmlCacheOrSource(@"CachesChanges\" + changeId + ".osc",
					() => { Thread.Sleep(10000); return OsmApiClient.GetChangesetDownload(changeId).Result; });
				var newElements = fullChange.GetElements();
				elements.AddRange(newElements);
			}

			return elements.ToArray();
		}

		public static Changeset[] GetUsersChangesets(long userId)
		{
			var changes = new List<Changeset>();
			var oneDay = TimeSpan.FromDays(1);
			var newChanges = new Changeset[0];
			do
			{
				var before = newChanges.DefaultIfEmpty(new Changeset() { CreatedAt = DateTime.UtcNow }).Min(c => c.CreatedAt.Value);
				newChanges = OsmApiClient.QueryChangesets(null, userId, null, DateTime.MinValue, before, false, false, null).Result;
				Thread.Sleep(10000);
				if (newChanges != null) changes.AddRange(newChanges);
			} while (newChanges?.Length == 100);

			return changes.ToArray();
		}

		public static long[] GetUsersChangesetIds(long userId)
		{
			var changeIds = FileSerializer.ReadJsonCacheOrSource(@"CachesChanges\" + userId + ".userChangeIds",
				() => GetUsersChangesets(userId).Select(c => c.Id.Value).ToArray());
			return changeIds;
		}

		public static TagsCollection GetCommitTags(string comment)
		{
			return new TagsCollection()
			{
				new Tag("comment", comment),
				new Tag("created_by", "OsmPipeline"),
				new Tag("created_by_library", Static.Config["created_by_library"]),
				new Tag("bot", "yes"),
				new Tag("osm_wiki_documentation_page", Static.Config["osm_wiki_documentation_page"]),
			};
		}

		private static IEnumerable<OsmGeo> OpenFile(string path)
		{
			var extension = Path.GetExtension(path);

			if (extension.Equals(".pbf", StringComparison.OrdinalIgnoreCase))
			{
				// Warning: IDisposable not disposed.
				return new OsmSharp.Streams.PBFOsmStreamSource(new FileInfo(path).OpenRead());
			}
			else if (extension.Equals(".osm", StringComparison.OrdinalIgnoreCase))
			{
				return FileSerializer.ReadXml<Osm>(path).GetElements();
			}

			throw new NotImplementedException(extension);
		}
	}
}
