using OsmPipeline.Fittings;
using OsmSharp;
using OsmSharp.API;
using OsmSharp.Changesets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

		// nohousenumber=yes
		// consider just leaving out all addr:unit=*
		// option to moveNode or not. (Do you trust E911 locations more than OSM?)
		// MatchWidth list to handle multi-match

		// Maybe fix addr:street punctuation on elements that I didn't add or update
		// Maybe fix addr:street should be addr:place on elements that I didn't add or update
		// Maybe fix nodes merging into buildings on elements that I didn't add or update
		// Maybe review all the names I added?
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
					var reference = References.Fetch(Municipality).Result;
					FileSerializer.WriteXml(Municipality + "/Reference.osm", reference);
					File.Delete(Municipality + "/Conflated.osc");
				}
				else if (Is(userInput, "review ref"))
				{
					var reference = GetReference();
					References.Report(reference.GetElements().ToArray());
				}
				else if (Is(userInput, "subject"))
				{
					var reference = GetReference();
					var subject = Subjects.GetElementsInBoundingBox(reference.Bounds.ExpandBy(15));
					FileSerializer.WriteXml(Municipality + "/Subject.osm", subject);
					File.Delete(Municipality + "/Conflated.osc");
					Console.WriteLine("ChangeId high watermark: " + subject.GetHighestChangeSetId());
				}
				else if (Is(userInput, "conflate"))
				{
					DoConflate();
				}
				else if (Is(userInput, "review con"))
				{
					Conflate.Review(Municipality);
				}
				else if (Is(userInput, "review"))
				{
					OpenJosm();
				}
				else if (Is(userInput, "list"))
				{
					var key = userInput.Split(" ")[1];
					var reference = GetReference();
					var values = reference.GetElements().Where(e => e.Tags.ContainsKey(key)).Select(e => "\n\t" + e.Tags[key]).GroupBy(n => n).ToArray();

					Console.WriteLine(string.Concat(values.Select(v => v.Key + "\tx" + v.Count())));
				}
				else if (Is(userInput, "filter"))
				{
					var key = userInput.Split(" ")[1];
					var reference = GetReference();
					DoFilter(key, reference.GetElements());
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
					var selection = review.GetElements()
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
					var tag = userInput.Split(' ', 2)[1].Replace("maineE911id=", "");
					Static.Municipalities[Municipality].BlackTags.Add(tag);
					Static.Municipalities[Municipality].BlackTags = Static.Municipalities[Municipality].BlackTags.Distinct().ToList();
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
					var change = FileSerializer.ReadXml<OsmChange>(Municipality + "/Conflated.osc");
					var results = Subjects.UploadChange(change, Municipality).Result;
					Static.Municipalities[Municipality].ChangeSetIds.AddRange(results);
					Static.Municipalities[Municipality].ImportDate = DateTime.UtcNow;
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
					Console.WriteLine("Finished!");

					Next();
				}
				else if (Is(userInput, "skip"))
				{
					Static.Municipalities[Municipality].Notes += " SKIPPED";
					Static.Municipalities[Municipality].ChangeSetIds.Add(-1);
					FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
				}
				else if (Is(userInput, "next"))
				{
					Next();
				}
				else if (Is(userInput, "switch"))
				{
					Municipality = ChooseMunicipality();
					ShowProgress();
				}
				else if (Is(userInput, "folder"))
				{
					OpenExplorer();
				}
				else if (Is(userInput, "remind"))
				{
					var parts = userInput.Split(' ', 3);
					var id = long.Parse(parts[1].Replace("maineE911id=", ""));
					var reference = GetReference();
					var element = reference.Nodes.First(e => Math.Abs(e.Id.Value) == Math.Abs(id));
					var message = parts.Length > 2 ? parts[2] : "The addresses imported on this neighborhood need to be aligned with the correct buildings";
					Subjects.CreateNote(element.Latitude.Value, element.Longitude.Value, message).Wait();
				}
				else if (Is(userInput, "help"))
				{
					Console.WriteLine("Options:");
					Console.WriteLine("\tSwitch");
					Console.WriteLine("\tNext");
					Console.WriteLine("\tReference");
					Console.WriteLine("\tReview Reference");
					Console.WriteLine("\tSubject");
					Console.WriteLine("\tConflate");
					Console.WriteLine("\tReview Conflate");
					Console.WriteLine("\tReview");
					Console.WriteLine("\tList [key]");
					Console.WriteLine("\tFilter [key]");
					Console.WriteLine("\t\t[Y/N/AUnit/Descr/Build/Move]");
					Console.WriteLine("\tNote [message]");
					Console.WriteLine("\tWhitelist [###]<,[###]...>");
					Console.WriteLine("\tWhiteAll");
					Console.WriteLine("\tBlacklist [###]<,[###]...>");
					Console.WriteLine("\tBlackTag [###].[key] || *.[key]=[value]");
					Console.WriteLine("\tIgnore [###]<,[###]...>");
					Console.WriteLine("\tRemind [ref###] <message>");
					Console.WriteLine("\tCommit");
				}
				else Console.WriteLine("What?");

				Console.WriteLine("Done");
			}
		}

		private void OpenJosm()
		{
			var reviewFiles = new[] {
				$"Conflated.Review.osm",
				$"Conflated.Modify.osm",
				$"Conflated.Delete.osm",
				$"Conflated.Create.osm",
				$"Reference.osm",
				$"Subject.osm"
			};
			var args = string.Join(" ", reviewFiles
				.Reverse() // To get the layer order correct in JOSM.
				.Select(f => $"{Municipality}/{f}")
				.Where(File.Exists)
				.Select(f => $"\"{f}\"")); //Quotes, because Municipality could have a space.
			if (args.Length == 0) Console.WriteLine("But there aren't any files :(");
			else Process.Start(Static.Config["JosmPath"], args);
		}

		private void Next()
		{
			Municipality = Static.Municipalities.Values.First(m => !m.ChangeSetIds.Any()).Name;
			Console.Clear();
			ShowProgress();
			Console.WriteLine("Switching to " + Municipality);
			DoConflate();
		}

		private Osm GetReference()
		{
			return FileSerializer.ReadXmlCacheOrSource(Municipality + "/Reference.osm",
				() => References.Fetch(Municipality)).Result;
		}

		private Osm GetSubject(Bounds bounds = null)
		{
			var subject = FileSerializer.ReadXmlCacheOrSource(Municipality + "/Subject.osm",
				() => Subjects.GetElementsInBoundingBox(bounds));
			Console.WriteLine("ChangeId high watermark: " + subject.GetHighestChangeSetId());
			return subject;
		}

		private void DoConflate()
		{
			var reference = GetReference();
			var subject = GetSubject(reference.Bounds.ExpandBy(15));
			var Change = Conflate.Merge(reference, subject, Municipality, Static.Municipalities[Municipality]);
			FileSerializer.WriteXml(Municipality + "/Conflated.osc", Change);
			References.Report(reference.GetElements().ToArray());
		}

		private void OpenExplorer()
		{
			Process.Start(Environment.GetEnvironmentVariable("WINDIR") + @"\explorer.exe", Environment.CurrentDirectory + "\\" + Municipality);
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
			Console.WriteLine($"{(int)(100*changed / Static.Municipalities.Count)}%: {changed} of {Static.Municipalities.Count} ({skipped} skipped)");
		}

		public void DoFilter(string key, IEnumerable<OsmGeo> elements)
		{
			var values = elements.Where(e => e.Tags.ContainsKey(key)).Select(e => e.Tags[key]).Distinct().OrderBy(v => v).ToArray();
			foreach (var value in values)
			{
				Console.WriteLine("?\t" + value);
				var choice = char.ToUpper(Console.ReadKey(true).KeyChar);
				if (choice == 'N')
				{
					Static.Municipalities[Municipality].BlackTags.Add($"*.{key}={value}");
				}
				else if (choice == 'U')
				{
					Static.Municipalities[Municipality].BlackTags.Add($"*.{key}={value}~addr:unit");
				}
				else if (choice == 'D')
				{
					Static.Municipalities[Municipality].BlackTags.Add($"*.{key}={value}~description");
				}
				else if (choice == 'B')
				{
					Static.Municipalities[Municipality].BlackTags.Add($"*.{key}={value}~building");
				}
				else if (choice == 'M')
				{
					Console.WriteLine($"Move '{value}' to what key?");
					var newKey = Console.ReadLine();
					Static.Municipalities[Municipality].BlackTags.Add($"*.{key}={value}~{newKey}");
				}
				else if (choice == 'A')
				{
					Static.Municipalities[Municipality].BlackTags.Add($"*.{key}={value}~alt_name");
				}
				else if (choice == 'Y')
				{

				}
				else
				{
					Console.WriteLine("Canceling");
					return;
				}
			}

			FileSerializer.WriteJson("MaineMunicipalities.json", Static.Municipalities);
		}
	}
}
