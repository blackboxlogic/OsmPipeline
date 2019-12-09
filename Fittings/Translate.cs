using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsmPipeline.Fittings
{
	public static class Translate
	{
		public static IEnumerable<OsmGeo> OsmToGeos(this Osm osm)
		{
			return new OsmGeo[][] { osm.Nodes, osm.Ways, osm.Relations }
				.Where(a => a != null)
				.SelectMany(a => a);
		}

		public static OsmChange GeosToChange(IEnumerable<OsmGeo> create, IEnumerable<OsmGeo> modify,
			IEnumerable<OsmGeo> delete, string generator, double? version = .6)
		{
			return new OsmChange()
			{
				Generator = generator,
				Version = version,
				Create = create?.ToArray(),
				Modify = modify?.ToArray(),
				Delete = delete?.ToArray()
			};
		}

		public static Osm AsOsm(this IEnumerable<OsmGeo> geos, double? version = .6, string generator = null)
		{
			return new Osm()
			{
				Nodes = geos.OfType<Node>().ToArray(),
				Ways = geos.OfType<Way>().ToArray(),
				Relations = geos.OfType<Relation>().ToArray(),
				Version = version,
				Generator = generator
			};
		}
	}
}
