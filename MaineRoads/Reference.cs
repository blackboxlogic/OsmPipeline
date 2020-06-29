using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsmPipeline.Fittings;
using OsmSharp.API;
using OsmSharp.Complete;
using OsmSharp.Db;
using OsmSharp.Db.Impl;
using OsmSharp.Streams;

namespace MaineRoads
{
	public static class Reference
	{
		public static IEnumerable<Osm> Generate()
		{
			var osm = FileSerializer.ReadXml<Osm>(@"ReferenceRaw.osm");
			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm.GetElements()));
			var asD = osm.GetElements().ToDictionary(e => new OsmSharp.OsmGeoKey(e));

			var translated = Translate(osm);
			FileSerializer.WriteXml("ReferenceTranslated.osm", translated.AsOsm(index));

			ReverseOneWays(translated);

			var combined = Geometry.CombineSegments(translated);
			// Object Id could be element id after combine()
			FileSerializer.WriteXml("ReferenceTranslatedCombined.osm", combined.AsOsm(index));

			var slices = combined.SliceRecusive(1000).ToDictionary();
			var simpleSlices = slices.Select(kvp => kvp.Value.Select(e => e.ToSimple()).WithChildren(index).AsOsm(null, .6, kvp.Key));

			return simpleSlices;
		}

		private static void ReverseOneWays(CompleteWay[] ways)
		{
			foreach (var way in ways.Where(w => w.Tags.Contains("oneway", "-1")))
			{
				way.Nodes = way.Nodes.Reverse().ToArray();
				way.Tags["oneway"] = "yes";
			}
		}

		private static Osm AsOsm(this IEnumerable<CompleteOsmGeo> elements, IOsmGeoSource index)
		{
			return elements.Select(e => e.ToSimple()).WithChildren(index).AsOsm();
		}

		private static CompleteWay[] Translate(Osm osm)
		{
			using (var translator = new OsmTagsTranslator.Translator(osm.GetElements()))
			{
				translator.AddLookup(@"C:\Users\Alex\Desktop\Maine_E911_Roads-shp\Lookups\Directions.json");
				translator.AddLookup(@"C:\Users\Alex\Desktop\Maine_E911_Roads-shp\Lookups\StreetSuffixes.json");
				translator.AddLookup(@"C:\Users\Alex\Desktop\Maine_E911_Roads-shp\Lookups\RoadClasses.json");
				var sql = File.ReadAllText(@"C:\Users\Alex\Desktop\Maine_E911_Roads-shp\Queries\E911RoadsToOsmSchema.sql");
				return translator.QueryElementsWithChildren(sql)
					.ToComplete().OfType<CompleteWay>().ToArray();
			};
		}
	}
}
