using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Streams;

namespace MaineRoads
{
	public static partial class Static
	{
		public static IConfigurationRoot Config;
		public static ILoggerFactory LogFactory;
	}

	class Program
	{
		static void Main(string[] args)
		{
			Static.Config = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", false, true)
				.Build();
			Environment.CurrentDirectory += Static.Config["WorkingDirectory"];
			IServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddLogging(builder => builder.AddConsole().AddFilter(level => true));
			Static.LogFactory = serviceCollection.BuildServiceProvider().GetService<ILoggerFactory>();

			try
			{
				var scope = ReadBounds(@"Franklin\FranklinCountyBorder.osm");
				Go(scope);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}

			Console.ReadKey(true);
		}

		private static Bounds ReadBounds(string file)
		{
			var county = FileSerializer.ReadXml<Osm>(file);
			var scope = county.GetElements().AsOsm().Bounds;
			return scope;
		}

		private static void Go(Bounds scope = null)
		{
			//Geometry.DistanceMeters(
			//	new NetTopologySuite.Geometries.Coordinate(scope.MinLongitude.Value, scope.MinLatitude.Value),
			//	new NetTopologySuite.Geometries.Coordinate(scope.MaxLongitude.Value, scope.MaxLatitude.Value));
			var reference = Reference.Generate("ReferenceRawSimplified.osm", "Reference.osm", scope);
			Osm subject;
			using (var stream = File.OpenRead("Subject.pbf"))
			{
				subject = new PBFOsmStreamSource(stream).AsOsm();
			}

			var missingPublic = Conflate.FindMissingRoads(subject, reference.Where(w => !w.Tags.Contains("ownership", "private")).ToArray());
			FileSerializer.WriteXml("Franklin\\MissingPublic.osm", missingPublic);
			JOSM.MarkAsNeverUpload("Franklin\\MissingPublic.osm");

			var missingPrivate = Conflate.FindMissingRoads(subject, reference.Where(w => w.Tags.Contains("ownership", "private")).ToArray());
			FileSerializer.WriteXml("Franklin\\MissingPrivate.osm", missingPrivate);
			JOSM.MarkAsNeverUpload("Franklin\\MissingPrivate.osm");

			int i = 0;
			var referenceSlices = BoundsExtentions.SliceRecusive(reference, 200);

			foreach (var referenceSlice in referenceSlices)
			{
				var directory = i.ToString("0000");
				Directory.CreateDirectory(directory);
				FileSerializer.WriteXml($"{directory}\\Reference.osm", referenceSlice.Value.AsOsm());
				JOSM.MarkAsNeverUpload($"{directory}\\Reference.osm");

				//var subjectSlice = subject.FilterBox(bounds.MinLongitude.Value, bounds.MaxLatitude.Value,
				//		bounds.MaxLongitude.Value, bounds.MinLatitude.Value, true)
				//	.AsOsm();
				//FileSerializer.WriteXml($"{i}\\Subject.osm", subjectSlice);
				//var missing = Conflate.FindMissingPublicRoads(subjectSlice, referenceSlice);
				//FileSerializer.WriteXml($"{i}\\MissingPublicRoads.osm", missing);
				//var session = File.ReadAllText("Session.jos");
				// Transform session
				//File.WriteAllText($"{i}\\Session.jos", session);
				i++;
			}
		}


	}
}
