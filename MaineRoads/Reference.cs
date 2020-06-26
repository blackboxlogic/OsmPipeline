using System;
using System.IO;
using System.Linq;
using OsmPipeline.Fittings;
using OsmSharp.API;
using OsmSharp.Complete;
using OsmSharp.Streams;

namespace MaineRoads
{
	public static class Reference
	{
		public static string[] Generate()
		{
			// fetch
			var osm = FileSerializer.ReadXml<Osm>(@"C:\Users\Alex\Desktop\Maine_E911_Roads-shp\Maine_E911_Roads.osm");
			var translated = Translate(osm);
			var combined = Geometry.CombineSegments(translated);
			var split = combined.SliceRecusive(100).ToArray();
			var splitt = combined.SliceRecusive(1000).ToArray();
			// translate
			// simplify
			// slice by TOWN
			// Create JOSM session files
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
