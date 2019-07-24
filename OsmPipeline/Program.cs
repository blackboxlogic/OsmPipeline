using System;
using System.Linq;

namespace OsmPipeline
{
	class Program
	{
		static void Main(string[] args)
		{
			string query =
@"node
  ['phone'~'^([^0-9]*1)?([^0-9]*[0-9]){10}[^0-9]*$']
  ['phone'!~'^\\+1\\-[0-9]{3}\\-[0-9]{3}\\-[0-9]{4}$']
  (area: 3600063512);
out;";

			var response = OverpassAPI.Fetch(query);
			response.Wait();

			foreach (var node in response.Result.node)
			{
				var number = node.tag?.FirstOrDefault(t => t.k == "phone")?.v;
				if (number != null)
				{
					Console.WriteLine($"{node.id}:\t{number}\t-->\t{Phones.Fix(number)}");
					//Console.WriteLine(number);
				}
			}

			Console.ReadKey(true);
		}
	}
}
