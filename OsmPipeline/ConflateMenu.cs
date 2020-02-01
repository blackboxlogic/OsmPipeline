using BAMCIS.GeoJSON;
using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using static OsmPipeline.Fittings.GeoJsonAPISource;

namespace OsmPipeline
{
	public static partial class Static
	{
		public const string maineE911id = "maineE911id";
		public static Dictionary<string, Municipality> Municipalities;
		public static HttpClient HttpClient = new HttpClient();
	}

	public class ConflateMenu
	{
		static ConflateMenu()
		{
			// Increase Console input max length
			byte[] inputBuffer = new byte[2048];
			Stream inputStream = Console.OpenStandardInput(inputBuffer.Length);
			Console.SetIn(new StreamReader(inputStream, Console.InputEncoding, false, inputBuffer.Length));
		}

		Func<string, string, bool> Is = (a,b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
		private string Municipality;

		public void Main()
		{
			Static.Municipalities = FileSerializer.ReadJsonCacheOrSource("MaineMunicipalities.json",
				GetMunicipalities).Result;
			ShowProgress();
			Municipality = Static.Municipalities.Values.First(m => !m.ChangeSetIds.Any()).Name;
			Console.WriteLine("Starting in " + Municipality);

			while (true)
			{
				Console.Write("> ");
				var userInput = Console.ReadLine();

				if (Is(userInput, "reference"))
				{
					//Func<Feature, bool> filter = f => (f.Geometry as Point).Coordinates.Longitude >= -70.505;
					var Reference = References.Fetch(Municipality).Result;
					FileSerializer.WriteXml(Municipality + "/Reference.osm", Reference);
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "subject"))
				{
					var Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					var Subject = Subjects.GetElementsInBoundingBox(Reference.Bounds).Result;
					FileSerializer.WriteXml(Municipality + "/Subject.osm", Subject);
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "conflate"))
				{
					var Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					var Subject = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Subject.osm",
						() => Subjects.GetElementsInBoundingBox(Reference.Bounds)).Result;
					var Change = Conflate.Merge(Reference, Subject, Municipality,
						Static.Municipalities[Municipality].WhiteList,
						Static.Municipalities[Municipality].IgnoreList);
					FileSerializer.WriteXml(Municipality + "/Conflated.osc", Change);
				}
				else if (Is(userInput, "review"))
				{
					// Last item will appear top of JOSM.
					var args = string.Join(" ", new[] {
						$"\"{Municipality}/Subject.osm\"",
						$"\"{Municipality}/Reference.osm\"",
						$"\"{Municipality}/Conflated.Create.osm\"",
						$"\"{Municipality}/Conflated.Delete.osm\"",
						$"\"{Municipality}/Conflated.Modify.osm\"",
						$"\"{Municipality}/Conflated.Review.osm\""}.Where(f => File.Exists(f.Trim('"'))));
					if (args.Length == 0) Console.WriteLine("But there aren't any files :(");
					else System.Diagnostics.Process.Start(Static.Config["JosmPath"], args);
				}
				else if (Is(userInput, "list"))
				{
					var key = userInput.Split(" ")[1];
					var Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					var values = Reference.OsmToGeos().Where(e => e.Tags.ContainsKey(key)).Select(e => e.Tags[key]).Distinct().ToArray();

					Console.WriteLine(string.Join("\n\t", values));
				}
				else if (Is(userInput, "filter"))
				{
					var key = userInput.Split(" ")[1];
					var Reference = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
						() => References.Fetch(Municipality)).Result;
					var valuesElements = Reference.OsmToGeos().Where(e => e.Tags.ContainsKey(key)).GroupBy(e => e.Tags[key]).ToArray();
					foreach (var valueElements in valuesElements)
					{
						Console.WriteLine(valueElements.Key + "?");
						if (char.ToUpper(Console.ReadKey(true).KeyChar) == 'N')
						{
							foreach (var element in valueElements)
							{
								Static.Municipalities[Municipality].BlackTags.Add(element.Id + "." + key);
							}
						}
					}
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Reference.osm");
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "note"))
				{
					Static.Municipalities[Municipality].Notes += "/n" + userInput.Split(' ', 2)[1];
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
				}
				else if (Is(userInput, "WhiteAll"))
				{
					var review = FileSerializer.ReadXml<Osm>(Municipality + "/Conflated.Review.osm");
					var selection = review.OsmToGeos()
						.Where(e => e.Tags != null && e.Tags.ContainsKey(Static.maineE911id))
						.Select(e => e.Tags[Static.maineE911id])
						.SelectMany(id => id.Split(new char[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries))
						.Select(long.Parse)
						.Except(Static.Municipalities[Municipality].WhiteList)
						.ToArray();
					Static.Municipalities[Municipality].WhiteList.AddRange(selection);

					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "white"))
				{
					var selection = userInput.Split(' ', 2)[1]
						.Split(new char[] { ' ', ',', ';', '-', '=' }, StringSplitOptions.RemoveEmptyEntries)
						.Where(c => long.TryParse(c, out _))
						.Select(long.Parse)
						.Except(Static.Municipalities[Municipality].WhiteList)
						.ToArray();
					Static.Municipalities[Municipality].WhiteList.AddRange(selection);

					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "blacktag"))
				{
					var tag = userInput.Split(' ', 2)[1];
					Static.Municipalities[Municipality].BlackTags.Add(tag);
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Reference.osm");
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "black"))
				{
					AddToList(userInput.Split(' ', 2)[1], Static.Municipalities[Municipality].BlackList);
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					File.Delete(Municipality + "/Reference.osm");
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "ignore"))
				{
					AddToList(userInput.Split(' ', 2)[1], Static.Municipalities[Municipality].IgnoreList);
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
				}
				else if (Is(userInput, "commit"))
				{
					var Change = FileSerializer.ReadXml<OsmChange>(Municipality + "/Conflated.osc");
					var results = Subjects.UploadChange(Change, Municipality).Result;
					Static.Municipalities[Municipality].ChangeSetIds.AddRange(results);
					Static.Municipalities[Municipality].ImportDate = DateTime.UtcNow;
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					Console.WriteLine("Finished!");
				}
				else if (Is(userInput, "skip"))
				{
					Static.Municipalities[Municipality].Notes += " SKIPPED";
					Static.Municipalities[Municipality].ChangeSetIds.Add(-1);
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
				}
				else if (Is(userInput, "next"))
				{
					Municipality = Static.Municipalities.Values.First(m => !m.ChangeSetIds.Any()).Name;
					Console.Clear();
					ShowProgress();
					Console.WriteLine("Switching to " + Municipality);
				}
				else if (Is(userInput, "switch"))
				{
					Municipality = ChooseMunicipality();
					ShowProgress();
				}
				else if (Is(userInput, "help"))
				{
					Console.WriteLine("Options:");
					Console.WriteLine("\tSwitch");
					Console.WriteLine("\tNext");
					Console.WriteLine("\tReference");
					Console.WriteLine("\tSubject");
					Console.WriteLine("\tConflate");
					Console.WriteLine("\tReview");
					Console.WriteLine("\tList [key]");
					Console.WriteLine("\tFilter [key]");
					Console.WriteLine("\tNote [message]");
					Console.WriteLine("\tWhitelist [###]<,[###]...>");
					Console.WriteLine("\tWhiteAll");
					Console.WriteLine("\tBlacklist [###]<,[###]...>");
					Console.WriteLine("\tBlackTag [###].[key]");
					Console.WriteLine("\tIgnore [###]<,[###]...>");
					Console.WriteLine("\tCommit");
				}
				else Console.WriteLine("What?");

				Console.WriteLine("Done");
			}
		}

		private void AddToList(string input, List<long> list)
		{
			var selection = input
				.Split(new char[] { ' ', ',', ';', '-', '=' }, StringSplitOptions.RemoveEmptyEntries)
				.Where(c => long.TryParse(c, out _))
				.Select(long.Parse)
				.Except(list)
				.ToArray();
			list.AddRange(selection);
		}

		private string ChooseMunicipality()
		{
			do
			{
				Console.WriteLine($"Which municipality?");
				var input = Console.ReadLine();
				var match = Static.Municipalities.Keys.FirstOrDefault(s => s.Equals(input, StringComparison.OrdinalIgnoreCase));
				if (match != null)
				{
					Console.WriteLine("Switching to " + match);
					if (Static.Municipalities[match].ChangeSetIds.Any())
					{
						Console.WriteLine($"WARNING: {match} already has changesets!");
					}
					return match;
				}
				var selection = Static.Municipalities.Keys.Where(m => m.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
				if (selection.Length == 1)
				{
					Console.WriteLine("Switching to " + selection[0]);
					if (Static.Municipalities[selection[0]].ChangeSetIds.Any())
					{
						Console.WriteLine($"WARNING: {selection[0]} already has changesets!");
					}
					return selection[0];
				}
				else
				{
					Console.WriteLine(string.Join("\n", selection));
				}
			} while (true);
		}

		public void ShowProgress()
		{
			var changed = Static.Municipalities.Values.Count(m => m.ChangeSetIds.Any(c => c != -1));
			var skipped = Static.Municipalities.Values.Count(m => m.ChangeSetIds.Any() && m.ChangeSetIds.All(c => c == -1));
			Console.WriteLine($"{changed} of {Static.Municipalities.Count} ({skipped} skipped)");
		}
	}
}
