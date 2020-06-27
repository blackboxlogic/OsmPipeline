using System;
using System.IO;
using System.Linq;
using OsmPipeline.Fittings;
using OsmSharp.API;
using OsmSharp.Complete;
using OsmSharp.Db.Impl;
using OsmSharp.Streams;

namespace MaineRoads
{
	public static class Reference
	{
		public static string[] Generate()
		{
			var osm = FileSerializer.ReadXml<Osm>(@"C:\Users\Alex\Desktop\Maine_E911_Roads-shp\Maine_E911_Roads.osm");
			var translated = Translate(osm);
			var combined = Geometry.CombineSegments(translated);
			var slices = combined.SliceRecusive(1000).ToArray();

			var index = OsmSharp.Db.Impl.Extensions.CreateSnapshotDb(new MemorySnapshotDb(osm.GetElements()));
			var firstSlice = slices[0].Select(e => e.ToSimple()).WithChildren(index).AsOsm();
			FileSerializer.WriteXml("firstSlice.osm", firstSlice);
			//foreach (var slice in slices)
			//{
			//	// Create JOSM session files
			//}

			// recruit/discuss
			return null;
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
