using OsmPipeline.Fittings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static OsmPipeline.Fittings.GeoJsonAPISource;

namespace OsmPipeline
{
	public static partial class Static
	{
		public const string maineE911id = "maineE911id";
		public static Dictionary<string, Municipality> Municipalities;
	}

	// review file items lost their Ids? Reference and subject have to be read from file to confate! not from memory
	// split large municipalites by zip?
	// separate translate and simplify
	public class ConflateMenu
	{
		Func<string, string, bool> Is = (a,b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase);

		private string Municipality;
		private OsmSharp.API.Osm Reference;
		private OsmSharp.API.Osm Subject;
		private OsmSharp.Changesets.OsmChange Change;

		public void Main()
		{
			Static.Municipalities = FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GetMunicipalities).Result;
			Municipality = "Westbrook"; //ChooseMunicipality();

			while (true)
			{
				Console.Write("> ");
				var userInput = Console.ReadLine();

				if (Is(userInput, "reference"))
				{
					Reference = References.Fetch(Municipality).Result;
					FileSerializer.WriteXml(Municipality + "/Reference.osm", Reference);
				}
				else if (Is(userInput, "subject"))
				{
					Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					Subject = Subjects.GetElementsInBoundingBox(Reference.Bounds).Result;
					FileSerializer.WriteXml(Municipality + "/Subject.osm", Subject);
				}
				else if (Is(userInput, "conflate"))
				{
					Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					Subject = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Subject.osm",
						() => Subjects.GetElementsInBoundingBox(Reference.Bounds)).Result;
					Change = Conflate.Merge(Reference, Subject, Municipality);
					FileSerializer.WriteXml(Municipality + "/Conflated.osc", Change);
				}
				else if (Is(userInput, "review"))
				{
					// Open JOSM with review layers
				}
				if (Is(userInput, "white"))
				{
					var selection = userInput.Split(' ', 2)[1]
						.Split(new char[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries)
						.Where(c => long.TryParse(c, out _))
						.Select(long.Parse)
						.Except(Static.Municipalities[Municipality].WhiteList)
						.ToArray();
					Static.Municipalities[Municipality].WhiteList.AddRange(selection);

					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
				}
				else if (Is(userInput, "black"))
				{
					var selection = userInput.Split(' ', 2)[1]
						.Split(new char[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries)
						.Where(c => long.TryParse(c, out _))
						.Select(long.Parse)
						.Except(Static.Municipalities[Municipality].BlackList)
						.ToArray();
					Static.Municipalities[Municipality].BlackList.AddRange(selection);
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Reference.osm");
				}
				else if (Is(userInput, "commit"))
				{
					// This is commented out so I don't accidentally commit changes to OSM.
					//if (Change == null) NO!
					//var results = await Subject.UploadChange(Change, Municipality);
					//Static.Municipalities[Municipality].ChangeSetIds.Add(results);
					//Static.Municipalities[Municipality].ImportDate = DateTime.UtcNow;
					//FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					//Console.WriteLine("Finished!");
				}
				else if (Is(userInput, "switch"))
				{
					Change = null;
					Subject = null;
					Reference = null;
					ChooseMunicipality();
				}
				else if (Is(userInput, "help"))
				{
					Console.WriteLine("Options:");
					Console.WriteLine("\tSwitch");
					Console.WriteLine("\tReference");
					Console.WriteLine("\tSubject");
					Console.WriteLine("\tConflate");
					Console.WriteLine("\tReview");
					Console.WriteLine("\tWhitelist [###]<,[###]...>");
					Console.WriteLine("\tBlacklist [###]<,[###]...>");
					Console.WriteLine("\tCommit");
				}
				else Console.WriteLine("What?");

				Console.WriteLine("Done");
			}
		}

		string ChooseMunicipality()
		{
			do
			{
				Console.WriteLine($"Which municipality?");
				var input = Console.ReadLine();
				var selection = Static.Municipalities.Keys.Where(m => m.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
				if (selection.Length == 1)
				{
					Console.Write("Switching to " + selection[0]);
					return selection[0];
				}
				else
				{
					Console.Write(string.Join("\n", selection));
				}
			} while (true);
		}
	}
}
