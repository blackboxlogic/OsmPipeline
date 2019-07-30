using System;
using System.Linq;
using System.Xml.Serialization;

namespace OsmPipeline
{
	class Program
	{
		private const ulong westbrook = 132501 + 3600000000;
		private const ulong maine = 63512 + 3600000000;
		private const ulong frenchtown = 707032430 + 2400000000;

		static void Main(string[] args)
		{
			string query =
@"node
  ['phone'~'^([^0-9]*1)?([^0-9]*[0-9]){10}[^0-9]*$']
  ['phone'!~'^\\+1\\-[0-9]{3}\\-[0-9]{3}\\-[0-9]{4}$']
  (area: 3600132501);
out meta;";
			var response = OverpassAPI.Fetch(query);
			response.Wait();
			foreach (var node in response.Result.Nodes)
			{
				var number = node.Tags["phone"];
				var fixedPhone = Phones.Fix(number);
				Console.WriteLine($"{node.Id}:\t{number}\t-->\t{fixedPhone}");
				node.Tags["phone"] = fixedPhone;
			}

			//Console.ReadKey(true);
			//get();
			Console.ReadKey(true);
		}

		public static void get()
		{

			OsmSharp.IO.API.Client client = new OsmSharp.IO.API.Client("https://www.openstreetmap.org/api/0.6/");
			var noder = client.GetNode("5045468981");
			noder.Wait();
			var node = noder.Result;
		}
	}
}
